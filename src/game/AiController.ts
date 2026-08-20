import * as THREE from 'three';
import { GAME_CONFIG, type DifficultyId } from '../config/gameConfig';

export interface AiWorldState {
  puckPosition: THREE.Vector3;
  puckVelocity: THREE.Vector3;
  malletPosition: THREE.Vector3;
}

export class AiController {
  private readonly target = new THREE.Vector3();
  private readonly noisyTarget = new THREE.Vector3();
  private decisionTimer = 0;
  private noiseX = 0;
  private bankDirection = 1;
  private attemptingBankShot = false;

  constructor(private difficulty: DifficultyId) {}

  setDifficulty(difficulty: DifficultyId): void {
    this.difficulty = difficulty;
    this.decisionTimer = 0;
  }

  reset(): void {
    this.decisionTimer = 0;
    this.noiseX = 0;
    this.bankDirection = Math.random() < 0.5 ? -1 : 1;
    this.attemptingBankShot = false;
  }

  update(deltaSeconds: number, state: AiWorldState): THREE.Vector3 {
    const tuning = GAME_CONFIG.ai[this.difficulty];
    this.decisionTimer -= deltaSeconds;
    if (this.decisionTimer <= 0) {
      this.decisionTimer = tuning.reactionSeconds;
      this.noiseX = (Math.random() * 2 - 1) * tuning.positionError;
      this.attemptingBankShot = Math.random() < tuning.bankShotChance;
      if (this.attemptingBankShot && Math.random() < 0.35) this.bankDirection *= -1;
      this.chooseTarget(state);
      this.noisyTarget.copy(this.target);
      this.noisyTarget.x += this.noiseX;
    }

    const maxStep = tuning.maxSpeed * deltaSeconds;
    const deltaX = this.noisyTarget.x - state.malletPosition.x;
    const deltaZ = this.noisyTarget.z - state.malletPosition.z;
    const distance = Math.hypot(deltaX, deltaZ);
    if (distance <= maxStep || distance === 0) return this.noisyTarget.clone();
    return new THREE.Vector3(
      state.malletPosition.x + (deltaX / distance) * maxStep,
      0,
      state.malletPosition.z + (deltaZ / distance) * maxStep,
    );
  }

  get activity(): number {
    return GAME_CONFIG.ai[this.difficulty].aggression;
  }

  private chooseTarget(state: AiWorldState): void {
    const tuning = GAME_CONFIG.ai[this.difficulty];
    const halfLength = GAME_CONFIG.table.length / 2;
    const halfWidth = GAME_CONFIG.table.width / 2;
    const safeX = halfWidth - GAME_CONFIG.mallet.radius - 0.03;
    const homeZ = halfLength * tuning.defenseDepth;
    const puck = state.puckPosition;
    const velocity = state.puckVelocity;
    const approaching = velocity.z > 0.08;
    const puckOnAiHalf = puck.z > -0.03;

    let predictedX = puck.x + velocity.x * tuning.predictionSeconds;
    predictedX = this.reflectWithinRails(predictedX, safeX);

    if (approaching) {
      const timeToDefense = Math.max(0, (homeZ - puck.z) / Math.max(velocity.z, 0.1));
      predictedX = this.reflectWithinRails(puck.x + velocity.x * Math.min(timeToDefense, tuning.predictionSeconds), safeX);
      this.target.set(predictedX, 0, homeZ);
      return;
    }

    if (puckOnAiHalf && Math.random() < tuning.aggression) {
      const attackDepth = THREE.MathUtils.clamp(
        puck.z + GAME_CONFIG.mallet.radius + GAME_CONFIG.puck.radius + 0.025,
        0.12,
        halfLength - GAME_CONFIG.mallet.radius - 0.05,
      );
      // Move to the opposite side of the desired lateral exit direction. The
      // resulting kinematic contact imparts a diagonal shot into the rail;
      // this is deliberately strongest for the archmage's high bank-shot chance.
      const bankOffset = this.attemptingBankShot ? this.bankDirection * 0.24 : 0;
      this.target.set(THREE.MathUtils.clamp(puck.x - bankOffset, -safeX, safeX), 0, attackDepth);
      return;
    }

    this.target.set(THREE.MathUtils.clamp(predictedX * 0.72, -safeX, safeX), 0, homeZ);
  }

  private reflectWithinRails(value: number, extent: number): number {
    let reflected = value;
    for (let index = 0; index < 4 && Math.abs(reflected) > extent; index += 1) {
      reflected = reflected > extent ? extent * 2 - reflected : -extent * 2 - reflected;
    }
    return THREE.MathUtils.clamp(reflected, -extent, extent);
  }
}
