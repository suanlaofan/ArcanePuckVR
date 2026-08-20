import { GAME_CONFIG } from '../config/gameConfig';

export type Side = 'player' | 'ai';
export type MatchPhase = 'idle' | 'serving' | 'playing' | 'paused' | 'finished';

export interface MatchSnapshot {
  playerScore: number;
  aiScore: number;
  phase: MatchPhase;
  goldenGoal: boolean;
  winner: Side | null;
  lastScorer: Side | null;
  serveSecondsRemaining: number;
  serveToward: Side;
}

export type RulesEvent =
  | { type: 'serve'; toward: Side }
  | { type: 'score'; side: Side; goldenGoal: boolean }
  | { type: 'match-won'; winner: Side }
  | { type: 'paused' }
  | { type: 'resumed' };

export interface PlanarPosition {
  x: number;
  z: number;
}

export function detectGoal(position: PlanarPosition): Side | null {
  const halfLength = GAME_CONFIG.table.length / 2;
  const fullyWithinMouth =
    Math.abs(position.x) + GAME_CONFIG.puck.radius <= GAME_CONFIG.table.goalWidth / 2;
  if (!fullyWithinMouth) return null;
  if (position.z - GAME_CONFIG.puck.radius > halfLength) return 'player';
  if (position.z + GAME_CONFIG.puck.radius < -halfLength) return 'ai';
  return null;
}

export class GameRules {
  private state: MatchSnapshot = this.createInitialState();

  get snapshot(): Readonly<MatchSnapshot> {
    return this.state;
  }

  startMatch(): void {
    this.state = this.createInitialState();
    this.state.phase = 'serving';
    this.state.serveSecondsRemaining = GAME_CONFIG.match.serveDelaySeconds;
  }

  returnToIdle(): void {
    this.state = this.createInitialState();
  }

  restartMatch(): void {
    this.startMatch();
  }

  togglePause(): RulesEvent | null {
    if (this.state.phase === 'playing' || this.state.phase === 'serving') {
      this.state.phase = 'paused';
      return { type: 'paused' };
    }
    if (this.state.phase === 'paused') {
      this.state.phase = this.state.serveSecondsRemaining > 0 ? 'serving' : 'playing';
      return { type: 'resumed' };
    }
    return null;
  }

  tick(deltaSeconds: number): RulesEvent[] {
    if (this.state.phase !== 'serving') return [];
    this.state.serveSecondsRemaining = Math.max(0, this.state.serveSecondsRemaining - deltaSeconds);
    if (this.state.serveSecondsRemaining > 0) return [];
    this.state.phase = 'playing';
    return [{ type: 'serve', toward: this.state.serveToward }];
  }

  score(side: Side): RulesEvent[] {
    if (this.state.phase !== 'playing') return [];

    if (side === 'player') this.state.playerScore += 1;
    else this.state.aiScore += 1;
    this.state.lastScorer = side;

    const events: RulesEvent[] = [{ type: 'score', side, goldenGoal: this.state.goldenGoal }];
    const hasWinner =
      this.state.goldenGoal ||
      this.state.playerScore >= GAME_CONFIG.match.winScore ||
      this.state.aiScore >= GAME_CONFIG.match.winScore;

    if (hasWinner) {
      this.state.phase = 'finished';
      this.state.winner = side;
      events.push({ type: 'match-won', winner: side });
      return events;
    }

    this.state.goldenGoal =
      this.state.playerScore === GAME_CONFIG.match.goldenGoalAt &&
      this.state.aiScore === GAME_CONFIG.match.goldenGoalAt;
    this.state.serveToward = side === 'player' ? 'ai' : 'player';
    this.state.serveSecondsRemaining = GAME_CONFIG.match.serveDelaySeconds;
    this.state.phase = 'serving';
    return events;
  }

  /** Test/automation seam: puts a live match at an exact score without bypassing outcome rules. */
  setScoreForTesting(playerScore: number, aiScore: number): void {
    this.state.playerScore = Math.max(0, Math.floor(playerScore));
    this.state.aiScore = Math.max(0, Math.floor(aiScore));
    this.state.goldenGoal =
      this.state.playerScore === GAME_CONFIG.match.goldenGoalAt &&
      this.state.aiScore === GAME_CONFIG.match.goldenGoalAt;
  }

  requestNeutralReset(): void {
    if (this.state.phase !== 'playing') return;
    this.state.serveToward = this.state.lastScorer === 'player' ? 'ai' : 'player';
    this.state.serveSecondsRemaining = GAME_CONFIG.match.serveDelaySeconds * 0.75;
    this.state.phase = 'serving';
  }

  private createInitialState(): MatchSnapshot {
    return {
      playerScore: 0,
      aiScore: 0,
      phase: 'idle',
      goldenGoal: false,
      winner: null,
      lastScorer: null,
      serveSecondsRemaining: 0,
      serveToward: Math.random() < 0.5 ? 'player' : 'ai',
    };
  }
}
