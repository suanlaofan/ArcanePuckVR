import * as THREE from 'three';
import { GAME_CONFIG } from '../config/gameConfig';

export class DesktopInput {
  private readonly target = new THREE.Vector3(0, 0, -GAME_CONFIG.table.length * 0.28);
  private readonly pointerNdc = new THREE.Vector2();
  private charging = false;
  private chargeSeconds = 0;
  private keyboardX = 0;
  private keyboardZ = 0;

  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly camera: THREE.Camera,
    private readonly tableRoot: THREE.Object3D,
  ) {
    canvas.addEventListener('pointermove', this.onPointerMove);
    canvas.addEventListener('pointerdown', this.onPointerDown);
    window.addEventListener('pointerup', this.onPointerUp);
    window.addEventListener('keydown', this.onKeyDown);
    window.addEventListener('keyup', this.onKeyUp);
    canvas.addEventListener('contextmenu', this.preventContextMenu);
  }

  update(deltaSeconds: number): void {
    if (this.charging) this.chargeSeconds = Math.min(GAME_CONFIG.mallet.chargeSeconds, this.chargeSeconds + deltaSeconds);
    if (this.keyboardX !== 0 || this.keyboardZ !== 0) {
      const length = Math.hypot(this.keyboardX, this.keyboardZ) || 1;
      const step = (GAME_CONFIG.mallet.maxDesktopSpeed * deltaSeconds) / length;
      this.target.x += this.keyboardX * step;
      this.target.z += this.keyboardZ * step;
    }
    this.clampTarget();
  }

  getTarget(): THREE.Vector3 {
    return this.target;
  }

  get charge(): number {
    return this.chargeSeconds / GAME_CONFIG.mallet.chargeSeconds;
  }

  get isCharging(): boolean {
    return this.charging;
  }

  consumeChargeImpulse(): number {
    if (this.charging || this.chargeSeconds <= 0) return 0;
    const impulse = this.charge * GAME_CONFIG.mallet.chargeImpulse;
    this.chargeSeconds = 0;
    return impulse;
  }

  reset(): void {
    this.target.set(0, 0, -GAME_CONFIG.table.length * 0.28);
    this.charging = false;
    this.chargeSeconds = 0;
  }

  dispose(): void {
    this.canvas.removeEventListener('pointermove', this.onPointerMove);
    this.canvas.removeEventListener('pointerdown', this.onPointerDown);
    window.removeEventListener('pointerup', this.onPointerUp);
    window.removeEventListener('keydown', this.onKeyDown);
    window.removeEventListener('keyup', this.onKeyUp);
    this.canvas.removeEventListener('contextmenu', this.preventContextMenu);
  }

  private readonly onPointerMove = (event: PointerEvent): void => {
    const rect = this.canvas.getBoundingClientRect();
    this.pointerNdc.set(
      ((event.clientX - rect.left) / rect.width) * 2 - 1,
      -((event.clientY - rect.top) / rect.height) * 2 + 1,
    );
    const raycaster = new THREE.Raycaster();
    raycaster.setFromCamera(this.pointerNdc, this.camera);
    const planeY = new THREE.Plane(new THREE.Vector3(0, 1, 0), -this.tableRoot.position.y);
    const worldHit = new THREE.Vector3();
    if (!raycaster.ray.intersectPlane(planeY, worldHit)) return;
    this.tableRoot.worldToLocal(worldHit);
    this.target.set(worldHit.x, 0, worldHit.z);
    this.clampTarget();
  };

  private readonly onPointerDown = (event: PointerEvent): void => {
    if (event.button !== 0) return;
    this.charging = true;
    this.canvas.setPointerCapture?.(event.pointerId);
  };

  private readonly onPointerUp = (event: PointerEvent): void => {
    if (event.button !== 0) return;
    this.charging = false;
  };

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    if (event.repeat && event.code === 'Space') return;
    if (event.code === 'KeyA' || event.code === 'ArrowLeft') this.keyboardX = -1;
    if (event.code === 'KeyD' || event.code === 'ArrowRight') this.keyboardX = 1;
    if (event.code === 'KeyW' || event.code === 'ArrowUp') this.keyboardZ = 1;
    if (event.code === 'KeyS' || event.code === 'ArrowDown') this.keyboardZ = -1;
    if (event.code === 'Space') this.charging = true;
  };

  private readonly onKeyUp = (event: KeyboardEvent): void => {
    if ((event.code === 'KeyA' || event.code === 'ArrowLeft') && this.keyboardX < 0) this.keyboardX = 0;
    if ((event.code === 'KeyD' || event.code === 'ArrowRight') && this.keyboardX > 0) this.keyboardX = 0;
    if ((event.code === 'KeyW' || event.code === 'ArrowUp') && this.keyboardZ > 0) this.keyboardZ = 0;
    if ((event.code === 'KeyS' || event.code === 'ArrowDown') && this.keyboardZ < 0) this.keyboardZ = 0;
    if (event.code === 'Space') this.charging = false;
  };

  private readonly preventContextMenu = (event: MouseEvent): void => event.preventDefault();

  private clampTarget(): void {
    const halfWidth = GAME_CONFIG.table.width / 2 - GAME_CONFIG.mallet.radius - 0.025;
    const halfLength = GAME_CONFIG.table.length / 2 - GAME_CONFIG.mallet.radius - 0.04;
    this.target.x = THREE.MathUtils.clamp(this.target.x, -halfWidth, halfWidth);
    this.target.z = THREE.MathUtils.clamp(
      this.target.z,
      -halfLength,
      -GAME_CONFIG.mallet.radius - 0.018,
    );
  }
}
