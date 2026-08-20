import RAPIER from '@dimforge/rapier3d-compat';
import * as THREE from 'three';
import { GAME_CONFIG } from '../config/gameConfig';
import { detectGoal, type Side } from './GameRules';

export interface PhysicsSnapshot {
  puckPosition: THREE.Vector3;
  puckVelocity: THREE.Vector3;
  playerPosition: THREE.Vector3;
  aiPosition: THREE.Vector3;
  puckSpeed: number;
}

export interface PhysicsStepResult {
  goal: Side | null;
  resetRequired: boolean;
  playerHit: boolean;
}

const MALLET_ANCHOR_Y = 0;
const PUCK_CENTER_Y = GAME_CONFIG.puck.height / 2;

/**
 * Rapier-only table simulation. Rendering receives immutable-ish snapshots;
 * no Object3D or DOM node is owned by this class.
 */
export class TablePhysics {
  private readonly world: RAPIER.World;
  private readonly puck: RAPIER.RigidBody;
  private readonly player: RAPIER.RigidBody;
  private readonly ai: RAPIER.RigidBody;
  private readonly puckCollider: RAPIER.Collider;
  private readonly playerCollider: RAPIER.Collider;
  private stationarySeconds = 0;
  private playerHitCooldown = 0;
  private readonly puckPosition = new THREE.Vector3();
  private readonly puckVelocity = new THREE.Vector3();
  private readonly playerPosition = new THREE.Vector3();
  private readonly aiPosition = new THREE.Vector3();

  static async create(): Promise<TablePhysics> {
    await RAPIER.init();
    return new TablePhysics();
  }

  private constructor() {
    this.world = new RAPIER.World({ x: 0, y: 0, z: 0 });
    this.world.timestep = GAME_CONFIG.fixedStep;
    this.world.numSolverIterations = 8;
    this.world.maxCcdSubsteps = 3;

    this.createRails();

    const puckDesc = RAPIER.RigidBodyDesc.dynamic()
      .setTranslation(0, PUCK_CENTER_Y, 0)
      .setLinearDamping(GAME_CONFIG.puck.linearDamping)
      .setAngularDamping(GAME_CONFIG.puck.angularDamping)
      .setGravityScale(0)
      .enabledTranslations(true, false, true)
      .enabledRotations(false, true, false)
      .setCcdEnabled(true);
    this.puck = this.world.createRigidBody(puckDesc);
    const puckColliderDesc = RAPIER.ColliderDesc.cylinder(GAME_CONFIG.puck.height / 2, GAME_CONFIG.puck.radius)
      .setMass(GAME_CONFIG.puck.mass)
      .setFriction(GAME_CONFIG.puck.friction)
      .setRestitution(GAME_CONFIG.puck.restitution)
      .setActiveEvents(RAPIER.ActiveEvents.COLLISION_EVENTS);
    this.puckCollider = this.world.createCollider(puckColliderDesc, this.puck);

    this.player = this.createMallet(-GAME_CONFIG.table.length * 0.28);
    this.ai = this.createMallet(GAME_CONFIG.table.length * 0.28);
    this.playerCollider = this.player.collider(0);
  }

  resetPuck(toward: Side, speed = 0): void {
    const direction = toward === 'ai' ? 1 : -1;
    this.puck.setTranslation({ x: 0, y: PUCK_CENTER_Y, z: 0 }, true);
    this.puck.setLinvel({ x: (Math.random() * 2 - 1) * speed * 0.18, y: 0, z: direction * speed }, true);
    this.puck.setAngvel({ x: 0, y: 0, z: 0 }, true);
    this.stationarySeconds = 0;
  }

  setPlayerTarget(target: THREE.Vector3): void {
    this.moveMallet(this.player, target, 'player');
  }

  setAiTarget(target: THREE.Vector3): void {
    this.moveMallet(this.ai, target, 'ai');
  }

  getSnapshot(): PhysicsSnapshot {
    const puckTranslation = this.puck.translation();
    const puckVelocity = this.puck.linvel();
    const playerTranslation = this.player.translation();
    const aiTranslation = this.ai.translation();
    this.puckPosition.set(puckTranslation.x, puckTranslation.y, puckTranslation.z);
    this.puckVelocity.set(puckVelocity.x, puckVelocity.y, puckVelocity.z);
    this.playerPosition.set(playerTranslation.x, playerTranslation.y, playerTranslation.z);
    this.aiPosition.set(aiTranslation.x, aiTranslation.y, aiTranslation.z);
    return {
      puckPosition: this.puckPosition,
      puckVelocity: this.puckVelocity,
      playerPosition: this.playerPosition,
      aiPosition: this.aiPosition,
      puckSpeed: Math.hypot(puckVelocity.x, puckVelocity.z),
    };
  }

  step(deltaSeconds: number, playerChargeImpulse = 0): PhysicsStepResult {
    this.playerHitCooldown = Math.max(0, this.playerHitCooldown - deltaSeconds);
    this.world.step();
    this.clampPuckVelocity();
    const snapshot = this.getSnapshot();
    const result: PhysicsStepResult = { goal: null, resetRequired: false, playerHit: false };

    const goal = detectGoal(snapshot.puckPosition);
    if (goal) {
      result.goal = goal;
      return result;
    }

    if (
      Math.abs(snapshot.puckPosition.x) > GAME_CONFIG.table.width / 2 + GAME_CONFIG.puck.outOfBoundsMargin ||
      Math.abs(snapshot.puckPosition.z) > GAME_CONFIG.table.length / 2 + GAME_CONFIG.puck.outOfBoundsMargin ||
      !Number.isFinite(snapshot.puckPosition.x) ||
      !Number.isFinite(snapshot.puckPosition.z)
    ) {
      result.resetRequired = true;
      return result;
    }

    this.stationarySeconds =
      snapshot.puckSpeed < GAME_CONFIG.puck.resetSpeedThreshold
        ? this.stationarySeconds + deltaSeconds
        : 0;
    result.resetRequired = this.stationarySeconds >= GAME_CONFIG.puck.stationaryResetSeconds;

    if (this.playerHitCooldown <= 0 && this.isPlayerTouchingPuck()) {
      result.playerHit = true;
      this.playerHitCooldown = GAME_CONFIG.mallet.hitCooldownSeconds;
      if (playerChargeImpulse > 0) this.applyChargeImpulse(playerChargeImpulse, snapshot);
    }
    return result;
  }

  dispose(): void {
    this.world.free();
  }

  private createMallet(z: number): RAPIER.RigidBody {
    const body = this.world.createRigidBody(
      RAPIER.RigidBodyDesc.kinematicPositionBased()
        .setTranslation(0, MALLET_ANCHOR_Y, z)
        .enabledRotations(false, true, false),
    );
    const collider = RAPIER.ColliderDesc.cylinder(GAME_CONFIG.mallet.height / 2, GAME_CONFIG.mallet.radius)
      .setTranslation(0, GAME_CONFIG.mallet.height / 2, 0)
      .setFriction(0.04)
      .setRestitution(GAME_CONFIG.mallet.restitution)
      .setActiveEvents(RAPIER.ActiveEvents.COLLISION_EVENTS);
    this.world.createCollider(collider, body);
    return body;
  }

  private createRails(): void {
    const { width, length, goalWidth, wallThickness, wallHeight } = GAME_CONFIG.table;
    const halfWidth = width / 2;
    const halfLength = length / 2;
    const rail = (halfX: number, halfZ: number, x: number, z: number): void => {
      const desc = RAPIER.ColliderDesc.cuboid(halfX, wallHeight / 2, halfZ)
        .setTranslation(x, wallHeight / 2, z)
        .setFriction(0.02)
        .setRestitution(GAME_CONFIG.puck.restitution);
      this.world.createCollider(desc);
    };

    rail(wallThickness / 2, halfLength + wallThickness, -halfWidth - wallThickness / 2, 0);
    rail(wallThickness / 2, halfLength + wallThickness, halfWidth + wallThickness / 2, 0);

    const endSegment = (width - goalWidth) / 4;
    for (const z of [-halfLength - wallThickness / 2, halfLength + wallThickness / 2]) {
      rail(endSegment, wallThickness / 2, -(goalWidth / 2 + endSegment), z);
      rail(endSegment, wallThickness / 2, goalWidth / 2 + endSegment, z);
    }
  }

  private moveMallet(body: RAPIER.RigidBody, target: THREE.Vector3, side: Side): void {
    const halfWidth = GAME_CONFIG.table.width / 2 - GAME_CONFIG.mallet.radius - 0.025;
    const halfLength = GAME_CONFIG.table.length / 2 - GAME_CONFIG.mallet.radius - 0.04;
    const middleClearance = GAME_CONFIG.mallet.radius + 0.018;
    const z =
      side === 'player'
        ? THREE.MathUtils.clamp(target.z, -halfLength, -middleClearance)
        : THREE.MathUtils.clamp(target.z, middleClearance, halfLength);
    body.setNextKinematicTranslation({
      x: THREE.MathUtils.clamp(target.x, -halfWidth, halfWidth),
      y: MALLET_ANCHOR_Y,
      z,
    });
  }

  private clampPuckVelocity(): void {
    const velocity = this.puck.linvel();
    const speed = Math.hypot(velocity.x, velocity.z);
    if (speed <= GAME_CONFIG.puck.maxSpeed) return;
    const scale = GAME_CONFIG.puck.maxSpeed / speed;
    this.puck.setLinvel({ x: velocity.x * scale, y: 0, z: velocity.z * scale }, true);
  }

  private isPlayerTouchingPuck(): boolean {
    let touching = false;
    this.world.contactPair(this.playerCollider, this.puckCollider, () => {
      touching = true;
    });
    return touching;
  }

  private applyChargeImpulse(chargeImpulse: number, snapshot: PhysicsSnapshot): void {
    const direction = snapshot.puckPosition.clone().sub(snapshot.playerPosition);
    direction.y = 0;
    if (direction.lengthSq() < 0.0001) direction.set(0, 0, 1);
    direction.normalize();
    this.puck.applyImpulse(
      { x: direction.x * chargeImpulse, y: 0, z: direction.z * chargeImpulse },
      true,
    );
  }
}
