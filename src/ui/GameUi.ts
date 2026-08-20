import type { DifficultyId } from '../config/gameConfig';
import type { MatchSnapshot } from '../game/GameRules';

export interface UiHandlers {
  onChooseDifficulty: (difficulty: DifficultyId) => void;
  onPause: () => void;
  onResume: () => void;
  onRestart: () => void;
  onMenu: () => void;
  onCalibrate: () => void;
}

export type OverlayView = 'menu' | 'difficulty' | 'playing' | 'paused' | 'finished';

export class GameUi {
  readonly root: HTMLElement;
  private readonly menuLayer: HTMLElement;
  private readonly hudLayer: HTMLElement;
  private view: OverlayView = 'menu';
  private match: Readonly<MatchSnapshot> | null = null;
  private difficulty: DifficultyId = 'mage';
  private status = '选择难度后开始快速 AI 对局';
  private vrSupported = false;
  private vrButton: HTMLButtonElement | null = null;
  private matchRenderKey = '';

  constructor(container: HTMLElement, private readonly handlers: UiHandlers) {
    this.root = document.createElement('div');
    this.root.className = 'ui-root';
    this.root.innerHTML = '<div class="hud-layer"></div><div class="menu-layer"></div>';
    container.append(this.root);
    this.hudLayer = this.required('.hud-layer');
    this.menuLayer = this.required('.menu-layer');
    this.render();
  }

  setView(view: OverlayView): void {
    this.view = view;
    this.matchRenderKey = '';
    this.render();
  }

  setMatch(snapshot: Readonly<MatchSnapshot>, difficulty: DifficultyId): void {
    this.match = snapshot;
    this.difficulty = difficulty;
    const renderKey = [
      this.view,
      difficulty,
      snapshot.playerScore,
      snapshot.aiScore,
      snapshot.phase,
      snapshot.goldenGoal,
      snapshot.winner,
      Math.ceil(snapshot.serveSecondsRemaining),
    ].join('|');
    if (renderKey === this.matchRenderKey) return;
    this.matchRenderKey = renderKey;
    this.renderHud();
  }

  setStatus(message: string): void {
    if (message === this.status) return;
    this.status = message;
    this.renderHud();
  }

  setVrSupport(supported: boolean, onEnterVr: () => void): void {
    this.vrSupported = supported;
    this.vrButton?.remove();
    this.vrButton = document.createElement('button');
    this.vrButton.type = 'button';
    this.vrButton.className = 'xr-button';
    this.vrButton.textContent = supported ? '进入 VR' : '未检测到 VR';
    this.vrButton.disabled = !supported;
    this.vrButton.title = supported ? '进入 WebXR 沉浸式模式' : '当前浏览器或设备不支持 immersive-vr';
    if (supported) this.vrButton.addEventListener('click', onEnterVr);
    this.root.append(this.vrButton);
  }

  private render(): void {
    this.renderHud();
    this.renderMenu();
  }

  private renderHud(): void {
    const showHud = this.view !== 'menu' && this.view !== 'difficulty';
    this.hudLayer.classList.toggle('hidden', !showHud);
    if (!showHud || !this.match) return;

    const phaseLabel = this.match.goldenGoal
      ? '金球加时'
      : GAME_LABELS[this.difficulty];
    this.hudLayer.innerHTML = `
      <div class="top-hud">
        <div class="score-card" aria-label="比分">
          <div class="score-side"><span>你</span><strong class="score-value">${this.match.playerScore}</strong></div>
          <div class="score-separator">${phaseLabel}</div>
          <div class="score-side score-side--enemy"><strong class="score-value">${this.match.aiScore}</strong><span>AI</span></div>
        </div>
      </div>
      <div class="hud-actions">
        <button class="icon-button" type="button" data-action="calibrate" title="重置视角 / 校准桌面" aria-label="重置视角">&#8635;</button>
        <button class="icon-button" type="button" data-action="pause" title="暂停" aria-label="暂停">&#10074;&#10074;</button>
      </div>
      <div class="status-message">${this.escape(this.status)}</div>
    `;
    this.bind('[data-action="calibrate"]', 'click', this.handlers.onCalibrate);
    this.bind('[data-action="pause"]', 'click', this.handlers.onPause);
  }

  private renderMenu(): void {
    const showMenu = this.view !== 'playing';
    this.menuLayer.classList.toggle('hidden', !showMenu);
    if (!showMenu) return;

    if (this.view === 'menu') {
      this.menuLayer.innerHTML = `
        <section class="menu-panel" aria-labelledby="game-title">
          <p class="menu-eyebrow">MAGIC ACADEMY DUEL HALL</p>
          <h1 class="menu-title" id="game-title">Arcane Puck VR</h1>
          <p class="menu-subtitle">单手驾驭符文击槌，在附魔球桌上与 AI 巫师决斗。先得 7 分获胜。</p>
          <div class="menu-actions">
            <button class="rune-button" type="button" data-action="quick-match">快速 AI 对局</button>
          </div>
          <p class="desktop-help">桌面调试：鼠标或 WASD/方向键移动，按住左键或空格蓄力。${this.vrSupported ? ' 已检测到 WebXR。' : ''}</p>
        </section>`;
      this.bind('[data-action="quick-match"]', 'click', () => this.setView('difficulty'));
      return;
    }

    if (this.view === 'difficulty') {
      this.menuLayer.innerHTML = `
        <section class="menu-panel" aria-labelledby="difficulty-title">
          <p class="menu-eyebrow">快速 AI 对局</p>
          <h2 class="menu-heading" id="difficulty-title">选择对手</h2>
          <div class="difficulty-list">
            ${this.difficultyButton('apprentice', '◇', '反应较慢，进攻容易出错')}
            ${this.difficultyButton('mage', '✦', '攻守均衡，会做基础预判')}
            ${this.difficultyButton('archmage', '✹', '更强预判，会尝试借边进攻')}
          </div>
          <button class="rune-button rune-button--secondary" type="button" data-action="back">返回</button>
        </section>`;
      for (const difficulty of ['apprentice', 'mage', 'archmage'] as const) {
        this.bind(`[data-difficulty="${difficulty}"]`, 'click', () => this.handlers.onChooseDifficulty(difficulty));
      }
      this.bind('[data-action="back"]', 'click', () => this.setView('menu'));
      return;
    }

    if (this.view === 'paused') {
      this.menuLayer.innerHTML = `
        <section class="menu-panel compact-panel">
          <p class="menu-eyebrow">决斗已暂停</p>
          <h2 class="menu-heading">时间凝滞</h2>
          <div class="menu-actions">
            <button class="rune-button" type="button" data-action="resume">继续对局</button>
            <button class="rune-button rune-button--secondary" type="button" data-action="restart">重新开始</button>
            <button class="rune-button rune-button--secondary" type="button" data-action="menu">返回主菜单</button>
          </div>
        </section>`;
      this.bind('[data-action="resume"]', 'click', this.handlers.onResume);
      this.bind('[data-action="restart"]', 'click', this.handlers.onRestart);
      this.bind('[data-action="menu"]', 'click', this.handlers.onMenu);
      return;
    }

    if (this.view === 'finished' && this.match) {
      const won = this.match.winner === 'player';
      this.menuLayer.innerHTML = `
        <section class="menu-panel compact-panel">
          <p class="menu-eyebrow">${this.match.goldenGoal ? '金球已决胜' : '决斗结束'}</p>
          <h2 class="menu-heading">${won ? '你获得了胜利' : 'AI 巫师获胜'}</h2>
          <p class="result-score">${this.match.playerScore} : ${this.match.aiScore}</p>
          <div class="menu-actions">
            <button class="rune-button" type="button" data-action="restart">再来一局</button>
            <button class="rune-button rune-button--secondary" type="button" data-action="menu">返回主菜单</button>
          </div>
        </section>`;
      this.bind('[data-action="restart"]', 'click', this.handlers.onRestart);
      this.bind('[data-action="menu"]', 'click', this.handlers.onMenu);
    }
  }

  private difficultyButton(id: DifficultyId, glyph: string, detail: string): string {
    return `<button class="difficulty-button" type="button" data-difficulty="${id}">
      <span class="difficulty-glyph">${glyph}</span>
      <span class="difficulty-copy"><span class="difficulty-name">${GAME_LABELS[id]}</span><span class="difficulty-detail">${detail}</span></span>
    </button>`;
  }

  private required<T extends HTMLElement>(selector: string): T {
    const element = this.root.querySelector<T>(selector);
    if (!element) throw new Error(`Missing UI element: ${selector}`);
    return element;
  }

  private bind(selector: string, type: string, listener: EventListener): void {
    this.root.querySelector(selector)?.addEventListener(type, listener);
  }

  private escape(value: string): string {
    const node = document.createElement('span');
    node.textContent = value;
    return node.innerHTML;
  }
}

const GAME_LABELS: Record<DifficultyId, string> = {
  apprentice: '学徒',
  mage: '法师',
  archmage: '大法师',
};
