import * as THREE from 'three';
import { XRControllerModelFactory } from 'three/examples/jsm/webxr/XRControllerModelFactory.js';
import { GAME_CONFIG } from '../config/gameConfig';
import type { PhysicsSnapshot } from '../game/TablePhysics';
import {
  ArcaneParticleField,
  createAiWizardVisual,
  createArcanePuckVisual,
  createArcaneTableVisual,
  createDuelHallVisual,
  createPortalGoalVisual,
  createRuneMalletVisual,
  pulsePortalGoal,
  setMalletCharge,
  updateAiWizardVisual,
  updateArcanePuckVisual,
  updatePortalGoalVisual,
  type GoalVisual,
  type MalletVisual,
  type PuckVisual,
  type WizardVisual,
} from '../game/VisualFactory';

export class SceneRenderer {
  readonly renderer: THREE.WebGLRenderer;
  readonly scene = new THREE.Scene();
  readonly camera: THREE.PerspectiveCamera;
  readonly tableRoot = new THREE.Group();
  readonly playerMallet: MalletVisual;
  readonly aiMallet: MalletVisual;
  readonly puck: PuckVisual;
  private readonly playerGoal: GoalVisual;
  private readonly aiGoal: GoalVisual;
  private readonly wizard: WizardVisual;
  private readonly particles: ArcaneParticleField;
  private elapsedSeconds = 0;
  private readonly cameraLookAt = new THREE.Vector3();

  constructor(container: HTMLElement) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.setSize(window.innerWidth, window.innerHeight);
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFShadowMap;
    this.renderer.xr.enabled = true;
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.12;
    container.append(this.renderer.domElement);

    this.scene.background = new THREE.Color(0x070a16);
    this.scene.fog = new THREE.FogExp2(0x080c1b, 0.052);
    this.camera = new THREE.PerspectiveCamera(50, window.innerWidth / window.innerHeight, 0.05, 70);
    this.camera.position.set(0, 3.05, -4.65);

    this.setupLighting();
    this.scene.add(createDuelHallVisual({ floorY: -1.12 }));
    this.scene.add(this.tableRoot);
    this.tableRoot.position.y = GAME_CONFIG.table.height;

    const table = createArcaneTableVisual({
      width: GAME_CONFIG.table.width,
      length: GAME_CONFIG.table.length,
      goalWidth: GAME_CONFIG.table.goalWidth,
      railHeight: GAME_CONFIG.table.wallHeight,
    });
    this.tableRoot.add(table);

    this.playerGoal = createPortalGoalVisual({
      side: 'player',
      width: GAME_CONFIG.table.goalWidth,
      height: GAME_CONFIG.table.goalHeight,
    });
    this.playerGoal.position.set(0, 0, -GAME_CONFIG.table.length / 2 - 0.016);
    this.aiGoal = createPortalGoalVisual({
      side: 'ai',
      width: GAME_CONFIG.table.goalWidth,
      height: GAME_CONFIG.table.goalHeight,
    });
    this.aiGoal.position.set(0, 0, GAME_CONFIG.table.length / 2 + 0.016);
    this.aiGoal.rotation.y = Math.PI;
    this.tableRoot.add(this.playerGoal, this.aiGoal);

    this.playerMallet = createRuneMalletVisual({
      radius: GAME_CONFIG.mallet.radius,
      height: GAME_CONFIG.mallet.height,
      owner: 'player',
    });
    this.aiMallet = createRuneMalletVisual({
      radius: GAME_CONFIG.mallet.radius,
      height: GAME_CONFIG.mallet.height,
      owner: 'ai',
    });
    this.puck = createArcanePuckVisual({ radius: GAME_CONFIG.puck.radius, height: GAME_CONFIG.puck.height });
    this.tableRoot.add(this.playerMallet, this.aiMallet, this.puck);

    this.wizard = createAiWizardVisual({ scale: 0.82 });
    this.wizard.position.set(0, -0.99, GAME_CONFIG.table.length / 2 + 1.05);
    this.wizard.rotation.y = Math.PI;
    this.scene.add(this.wizard);

    this.particles = new ArcaneParticleField({ bounds: new THREE.Vector3(4.4, 4.5, 5.5), count: 120 });
    this.particles.position.y = -0.95;
    this.scene.add(this.particles);
    this.addControllerVisuals();

    window.addEventListener('resize', this.onResize);
    this.updateDesktopCamera();
  }

  sync(snapshot: PhysicsSnapshot, deltaSeconds: number, playerCharge: number, aiActivity: number): void {
    this.elapsedSeconds += deltaSeconds;
    this.playerMallet.position.copy(snapshot.playerPosition);
    this.aiMallet.position.copy(snapshot.aiPosition);
    this.puck.position.copy(snapshot.puckPosition);
    setMalletCharge(this.playerMallet, playerCharge);
    updateArcanePuckVisual(this.puck, deltaSeconds, snapshot.puckSpeed);
    updatePortalGoalVisual(this.playerGoal, this.elapsedSeconds, deltaSeconds);
    updatePortalGoalVisual(this.aiGoal, this.elapsedSeconds, deltaSeconds);
    updateAiWizardVisual(this.wizard, this.elapsedSeconds, aiActivity);
    this.particles.update(deltaSeconds);
    if (!this.renderer.xr.isPresenting) this.updateDesktopCamera();
  }

  render(): void {
    this.renderer.render(this.scene, this.camera);
  }

  burstAt(localPosition: THREE.Vector3, strength = 1, isScore = false): void {
    const worldPosition = this.tableRoot.localToWorld(localPosition.clone());
    this.particles.worldToLocal(worldPosition);
    this.particles.burst(worldPosition, strength, isScore ? 48 : 18);
  }

  pulseGoal(side: 'player' | 'ai'): void {
    pulsePortalGoal(side === 'player' ? this.aiGoal : this.playerGoal);
  }

  calibrateTable(): void {
    if (this.renderer.xr.isPresenting) {
      const headset = this.renderer.xr.getCamera();
      const forward = new THREE.Vector3(0, 0, -1).applyQuaternion(headset.quaternion);
      forward.y = 0;
      forward.normalize();
      this.tableRoot.position.set(
        headset.position.x + forward.x * GAME_CONFIG.comfort.tableForwardDistance,
        headset.position.y - 0.76,
        headset.position.z + forward.z * GAME_CONFIG.comfort.tableForwardDistance,
      );
      return;
    }
    this.tableRoot.position.set(0, GAME_CONFIG.table.height, 0);
    this.camera.position.set(0, 3.05, -4.65);
  }

  dispose(): void {
    window.removeEventListener('resize', this.onResize);
    this.particles.dispose();
    this.renderer.dispose();
    this.renderer.domElement.remove();
  }

  private setupLighting(): void {
    const ambient = new THREE.HemisphereLight(0x9eaaff, 0x140c20, 1.15);
    this.scene.add(ambient);
    const key = new THREE.DirectionalLight(0xffdc9c, 2.2);
    key.position.set(-3.5, 6, -2.5);
    key.castShadow = true;
    key.shadow.mapSize.set(1024, 1024);
    key.shadow.camera.left = -5;
    key.shadow.camera.right = 5;
    key.shadow.camera.top = 5;
    key.shadow.camera.bottom = -5;
    this.scene.add(key);
    const cyan = new THREE.PointLight(0x46e8ff, 16, 7, 2);
    cyan.position.set(-2.4, 2.4, -1.8);
    this.scene.add(cyan);
    const violet = new THREE.PointLight(0xa579ff, 15, 7, 2);
    violet.position.set(2.4, 2.6, 1.8);
    this.scene.add(violet);
  }

  private addControllerVisuals(): void {
    const modelFactory = new XRControllerModelFactory();
    for (let index = 0; index < 2; index += 1) {
      const grip = this.renderer.xr.getControllerGrip(index);
      grip.add(modelFactory.createControllerModel(grip));
      this.scene.add(grip, this.renderer.xr.getController(index));
    }
  }

  private updateDesktopCamera(): void {
    const tableWorld = this.tableRoot.getWorldPosition(this.cameraLookAt);
    this.camera.lookAt(tableWorld.x, tableWorld.y - 0.04, tableWorld.z + 0.22);
  }

  private readonly onResize = (): void => {
    this.camera.aspect = window.innerWidth / window.innerHeight;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(window.innerWidth, window.innerHeight);
  };
}
