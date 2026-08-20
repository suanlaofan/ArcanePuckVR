import * as THREE from 'three';
import { GAME_CONFIG } from '../config/gameConfig';

interface HapticActuator {
  pulse(value: number, duration: number): Promise<boolean>;
}

export class XrInput {
  readonly controller: THREE.Group;
  readonly grip: THREE.Group;
  private readonly target = new THREE.Vector3();
  private readonly worldPosition = new THREE.Vector3();
  private inputSource: XRInputSource | null = null;
  private charging = false;
  private chargeSeconds = 0;
  private releasedCharge = false;
  private lastInteractionMs = 0;

  constructor(renderer: THREE.WebGLRenderer, index = 0) {
    this.controller = renderer.xr.getController(index);
    this.grip = renderer.xr.getControllerGrip(index);
    const events = this.controller as unknown as EventTarget;
    events.addEventListener('connected', this.onConnected);
    events.addEventListener('disconnected', this.onDisconnected);
    events.addEventListener('selectstart', this.onSelectStart);
    events.addEventListener('selectend', this.onSelectEnd);
  }

  update(deltaSeconds: number, tableRoot: THREE.Object3D): void {
    if (this.charging) this.chargeSeconds = Math.min(GAME_CONFIG.mallet.chargeSeconds, this.chargeSeconds + deltaSeconds);
    this.controller.getWorldPosition(this.worldPosition);
    tableRoot.worldToLocal(this.worldPosition);
    this.target.set(this.worldPosition.x, 0, this.worldPosition.z);
    const halfWidth = GAME_CONFIG.table.width / 2 - GAME_CONFIG.mallet.radius - 0.025;
    const halfLength = GAME_CONFIG.table.length / 2 - GAME_CONFIG.mallet.radius - 0.04;
    this.target.x = THREE.MathUtils.clamp(this.target.x, -halfWidth, halfWidth);
    this.target.z = THREE.MathUtils.clamp(this.target.z, -halfLength, -GAME_CONFIG.mallet.radius - 0.018);
  }

  getTarget(): THREE.Vector3 {
    return this.target;
  }

  get connected(): boolean {
    return this.inputSource !== null;
  }

  /** Lets the app choose the hand most recently used by the player. */
  get lastInteraction(): number {
    return this.lastInteractionMs;
  }

  get charge(): number {
    return this.chargeSeconds / GAME_CONFIG.mallet.chargeSeconds;
  }

  consumeChargeImpulse(): number {
    if (!this.releasedCharge || this.chargeSeconds <= 0) return 0;
    this.releasedCharge = false;
    const impulse = this.charge * GAME_CONFIG.mallet.chargeImpulse;
    this.chargeSeconds = 0;
    return impulse;
  }

  pulse(): void {
    const gamepad = this.inputSource?.gamepad as unknown as { hapticActuators?: readonly HapticActuator[] } | undefined;
    const actuator = gamepad?.hapticActuators?.[0];
    void actuator?.pulse(GAME_CONFIG.comfort.hapticIntensity, GAME_CONFIG.comfort.hapticDurationMs);
  }

  reset(): void {
    this.charging = false;
    this.chargeSeconds = 0;
    this.releasedCharge = false;
  }

  dispose(): void {
    const events = this.controller as unknown as EventTarget;
    events.removeEventListener('connected', this.onConnected);
    events.removeEventListener('disconnected', this.onDisconnected);
    events.removeEventListener('selectstart', this.onSelectStart);
    events.removeEventListener('selectend', this.onSelectEnd);
  }

  private readonly onConnected = (event: Event): void => {
    this.inputSource = (event as Event & { data?: XRInputSource }).data ?? null;
    this.lastInteractionMs = performance.now();
  };

  private readonly onDisconnected = (): void => {
    this.inputSource = null;
  };

  private readonly onSelectStart = (): void => {
    this.charging = true;
    this.releasedCharge = false;
    this.lastInteractionMs = performance.now();
  };

  private readonly onSelectEnd = (): void => {
    this.charging = false;
    this.releasedCharge = true;
    this.lastInteractionMs = performance.now();
  };
}
