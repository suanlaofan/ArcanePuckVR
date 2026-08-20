export type DifficultyId = 'apprentice' | 'mage' | 'archmage';

export interface AiTuning {
  label: string;
  reactionSeconds: number;
  maxSpeed: number;
  positionError: number;
  predictionSeconds: number;
  defenseDepth: number;
  aggression: number;
  bankShotChance: number;
}

export const GAME_CONFIG = {
  fixedStep: 1 / 90,
  table: {
    width: 2.2,
    length: 3.6,
    height: 0.82,
    wallThickness: 0.085,
    wallHeight: 0.14,
    goalWidth: 0.82,
    goalHeight: 0.44,
    goalDepth: 0.42,
  },
  puck: {
    radius: 0.092,
    height: 0.052,
    mass: 0.17,
    linearDamping: 0.16,
    angularDamping: 0.9,
    restitution: 0.93,
    friction: 0.02,
    maxSpeed: 5.6,
    resetSpeedThreshold: 0.075,
    stationaryResetSeconds: 4,
    outOfBoundsMargin: 0.55,
    serveSpeed: 1.05,
  },
  mallet: {
    radius: 0.145,
    height: 0.105,
    restitution: 0.72,
    maxDesktopSpeed: 3.6,
    maxVrSpeed: 4.5,
    chargeSeconds: 1.15,
    chargeImpulse: 0.62,
    hitCooldownSeconds: 0.12,
  },
  match: {
    winScore: 7,
    goldenGoalAt: 6,
    serveDelaySeconds: 1.05,
  },
  comfort: {
    tableForwardDistance: 1.18,
    hapticIntensity: 0.32,
    hapticDurationMs: 45,
  },
  ai: {
    apprentice: {
      label: '学徒',
      reactionSeconds: 0.28,
      maxSpeed: 1.45,
      positionError: 0.2,
      predictionSeconds: 0.14,
      defenseDepth: 0.67,
      aggression: 0.42,
      bankShotChance: 0.04,
    },
    mage: {
      label: '法师',
      reactionSeconds: 0.12,
      maxSpeed: 2.05,
      positionError: 0.075,
      predictionSeconds: 0.48,
      defenseDepth: 0.61,
      aggression: 0.67,
      bankShotChance: 0.16,
    },
    archmage: {
      label: '大法师',
      reactionSeconds: 0.055,
      maxSpeed: 2.7,
      positionError: 0.025,
      predictionSeconds: 0.92,
      defenseDepth: 0.57,
      aggression: 0.88,
      bankShotChance: 0.48,
    },
  } satisfies Record<DifficultyId, AiTuning>,
} as const;

export type GameConfig = typeof GAME_CONFIG;

