import { GAME_CONFIG, type DifficultyId } from '../config/gameConfig';
import { AiController } from '../game/AiController';
import { GameRules, type RulesEvent } from '../game/GameRules';
import { TablePhysics } from '../game/TablePhysics';
import { DesktopInput } from '../input/DesktopInput';
import { XrInput } from '../input/XrInput';
import { SceneRenderer } from '../render/SceneRenderer';
import { GameUi } from '../ui/GameUi';

export class ArcanePuckApp {
  private readonly scene: SceneRenderer;
  private readonly rules = new GameRules();
  private readonly ui: GameUi;
  private readonly desktopInput: DesktopInput;
  private readonly xrInputs: XrInput[];
  private readonly physics: TablePhysics;
  private ai = new AiController('mage');
  private difficulty: DifficultyId = 'mage';
  private lastFrameSeconds = performance.now() / 1000;
  private accumulator = 0;
  private pendingChargeImpulse = 0;
  private statusExpiry = 0;
  private destroyed = false;

  static async create(container: HTMLElement): Promise<ArcanePuckApp> {
    const physics = await TablePhysics.create();
    return new ArcanePuckApp(container, physics);
  }

  private constructor(container: HTMLElement, physics: TablePhysics) {
    this.physics = physics;
    this.scene = new SceneRenderer(container);
    this.desktopInput = new DesktopInput(this.scene.renderer.domElement, this.scene.camera, this.scene.tableRoot);
    this.xrInputs = [new XrInput(this.scene.renderer, 0), new XrInput(this.scene.renderer, 1)];

    this.ui = new GameUi(container, {
      onChooseDifficulty: this.startMatch,
      onPause: this.pause,
      onResume: this.resume,
      onRestart: this.restart,
      onMenu: this.returnToMenu,
      onCalibrate: this.calibrate,
    });
    this.ui.setMatch(this.rules.snapshot, this.difficulty);
    this.setupVrEntry();

    window.addEventListener('keydown', this.onGlobalKeyDown);
    this.scene.renderer.setAnimationLoop(this.frame);
  }

  dispose(): void {
    this.destroyed = true;
    this.scene.renderer.setAnimationLoop(null);
    window.removeEventListener('keydown', this.onGlobalKeyDown);
    this.desktopInput.dispose();
    this.xrInputs.forEach((input) => input.dispose());
    this.physics.dispose();
    this.scene.dispose();
  }

  private readonly startMatch = (difficulty: DifficultyId): void => {
    this.difficulty = difficulty;
    this.ai = new AiController(difficulty);
    this.rules.startMatch();
    this.resetRound(false);
    this.ui.setView('playing');
    this.ui.setStatus(`${GAME_CONFIG.ai[difficulty].label} AI 已就位，准备发球`);
    this.ui.setMatch(this.rules.snapshot, this.difficulty);
  };

  private readonly pause = (): void => {
    if (this.rules.togglePause()?.type !== 'paused') return;
    this.ui.setView('paused');
    this.ui.setMatch(this.rules.snapshot, this.difficulty);
  };

  private readonly resume = (): void => {
    if (this.rules.togglePause()?.type !== 'resumed') return;
    this.lastFrameSeconds = performance.now() / 1000;
    this.ui.setView('playing');
    this.ui.setMatch(this.rules.snapshot, this.difficulty);
  };

  private readonly restart = (): void => {
    this.rules.restartMatch();
    this.ai.reset();
    this.resetRound(false);
    this.ui.setView('playing');
    this.ui.setStatus('新的决斗开始');
    this.ui.setMatch(this.rules.snapshot, this.difficulty);
  };

  private readonly returnToMenu = (): void => {
    this.rules.returnToIdle();
    this.ai.reset();
    this.resetRound(false);
    this.ui.setView('menu');
    this.ui.setMatch(this.rules.snapshot, this.difficulty);
  };

  private readonly calibrate = (): void => {
    this.scene.calibrateTable();
    this.setTransientStatus('桌面高度和视角已校准', 2.2);
  };

  private readonly frame = (): void => {
    if (this.destroyed) return;
    const now = performance.now() / 1000;
    const deltaSeconds = Math.min(0.05, Math.max(0, now - this.lastFrameSeconds));
    this.lastFrameSeconds = now;
    this.update(deltaSeconds, now);
    this.scene.render();
  };

  private update(deltaSeconds: number, now: number): void {
    this.desktopInput.update(deltaSeconds);
    if (this.scene.renderer.xr.isPresenting) {
      this.xrInputs.forEach((input) => input.update(deltaSeconds, this.scene.tableRoot));
    }

    if (this.rules.snapshot.phase !== 'paused' && this.rules.snapshot.phase !== 'idle' && this.rules.snapshot.phase !== 'finished') {
      const events = this.rules.tick(deltaSeconds);
      this.handleRuleEvents(events);
      if (this.rules.snapshot.phase === 'playing') this.updateSimulation(deltaSeconds);
    }

    const snapshot = this.physics.getSnapshot();
    const activeXrInput = this.getActiveXrInput();
    const charge = this.scene.renderer.xr.isPresenting ? activeXrInput.charge : this.desktopInput.charge;
    this.scene.sync(snapshot, deltaSeconds, charge, this.ai.activity);
    this.ui.setMatch(this.rules.snapshot, this.difficulty);

    if (this.statusExpiry > 0 && now >= this.statusExpiry && this.rules.snapshot.phase === 'playing') {
      this.statusExpiry = 0;
      this.ui.setStatus(this.rules.snapshot.goldenGoal ? '金球：下一分决胜' : '鼠标 / 手柄移动击槌，按住蓄力');
    }
  }

  private updateSimulation(deltaSeconds: number): void {
    const activeXrInput = this.getActiveXrInput();
    const inputTarget = this.scene.renderer.xr.isPresenting ? activeXrInput.getTarget() : this.desktopInput.getTarget();
    this.physics.setPlayerTarget(inputTarget);
    const beforeStep = this.physics.getSnapshot();
    const aiTarget = this.ai.update(deltaSeconds, {
      puckPosition: beforeStep.puckPosition,
      puckVelocity: beforeStep.puckVelocity,
      malletPosition: beforeStep.aiPosition,
    });
    this.physics.setAiTarget(aiTarget);

    const releasedImpulse = this.scene.renderer.xr.isPresenting
      ? activeXrInput.consumeChargeImpulse()
      : this.desktopInput.consumeChargeImpulse();
    this.pendingChargeImpulse = Math.max(this.pendingChargeImpulse, releasedImpulse);

    this.accumulator = Math.min(this.accumulator + deltaSeconds, GAME_CONFIG.fixedStep * 5);
    while (this.accumulator >= GAME_CONFIG.fixedStep) {
      const result = this.physics.step(GAME_CONFIG.fixedStep, this.pendingChargeImpulse);
      this.accumulator -= GAME_CONFIG.fixedStep;
      if (result.playerHit) {
        const hitPosition = this.physics.getSnapshot().puckPosition.clone();
        this.scene.burstAt(hitPosition, 0.6 + this.pendingChargeImpulse * 1.4);
        if (this.scene.renderer.xr.isPresenting) activeXrInput.pulse();
        this.pendingChargeImpulse = 0;
      }
      if (result.goal) {
        this.handleRuleEvents(this.rules.score(result.goal));
        break;
      }
      if (result.resetRequired) {
        this.rules.requestNeutralReset();
        this.resetRound(false);
        this.setTransientStatus('圆盘异常或停滞，已重置到中线', 2.3);
        break;
      }
    }
  }

  private handleRuleEvents(events: RulesEvent[]): void {
    for (const event of events) {
      if (event.type === 'serve') {
        this.physics.resetPuck(event.toward, GAME_CONFIG.puck.serveSpeed);
        this.setTransientStatus(event.toward === 'ai' ? '圆盘向 AI 半场发出' : '防守：圆盘向你的半场发出', 1.8);
      }
      if (event.type === 'score') {
        const position = this.physics.getSnapshot().puckPosition.clone();
        this.scene.burstAt(position, 1.45, true);
        this.scene.pulseGoal(event.side);
        this.resetRound(false);
        this.ui.setStatus(event.side === 'player' ? '你得分了' : 'AI 巫师得分');
      }
      if (event.type === 'match-won') {
        this.physics.resetPuck('ai', 0);
        this.ui.setView('finished');
      }
    }
  }

  private resetRound(withServeVelocity: boolean): void {
    this.physics.resetPuck(
      this.rules.snapshot.serveToward,
      withServeVelocity ? GAME_CONFIG.puck.serveSpeed : 0,
    );
    this.desktopInput.reset();
    this.xrInputs.forEach((input) => input.reset());
    this.pendingChargeImpulse = 0;
    this.accumulator = 0;
  }

  private setTransientStatus(message: string, seconds: number): void {
    this.ui.setStatus(message);
    this.statusExpiry = performance.now() / 1000 + seconds;
  }

  private getActiveXrInput(): XrInput {
    const connectedInputs = this.xrInputs.filter((input) => input.connected);
    if (connectedInputs.length === 0) return this.xrInputs[0];
    return connectedInputs.reduce((latest, input) =>
      input.lastInteraction > latest.lastInteraction ? input : latest,
    );
  }

  private setupVrEntry(): void {
    const xr = navigator.xr;
    if (!xr) {
      this.ui.setVrSupport(false, () => undefined);
      return;
    }
    void xr.isSessionSupported('immersive-vr').then((supported) => {
      this.ui.setVrSupport(supported, () => void this.enterVr());
    });
  }

  private async enterVr(): Promise<void> {
    const xr = navigator.xr;
    if (!xr || this.scene.renderer.xr.isPresenting) return;
    try {
      const session = await xr.requestSession('immersive-vr', {
        optionalFeatures: ['local-floor', 'bounded-floor', 'hand-tracking', 'dom-overlay'],
        domOverlay: { root: this.ui.root },
      });
      await this.scene.renderer.xr.setSession(session);
      this.scene.calibrateTable();
    } catch (error) {
      console.error('Unable to enter WebXR session', error);
      this.setTransientStatus('无法进入 VR，请检查头显和浏览器授权', 3);
    }
  }

  private readonly onGlobalKeyDown = (event: KeyboardEvent): void => {
    if (event.code === 'Escape' || event.code === 'KeyP') {
      if (this.rules.snapshot.phase === 'paused') this.resume();
      else this.pause();
    }
    if (event.code === 'KeyR') this.calibrate();
  };
}
