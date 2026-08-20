/**
 * Names of the replaceable presentation assets.  The prototype deliberately
 * renders these slots with procedural meshes, but gameplay code must never
 * depend on a mesh name, material, or source format behind one of these slots.
 *
 * A Unity implementation can keep these exact keys in a ScriptableObject (or
 * Addressables labels) while reusing the rules/configuration layer unchanged.
 */
export const ASSET_SLOTS = {
  TABLE_MODEL: 'TABLE_MODEL',
  MALLET_MODEL: 'MALLET_MODEL',
  PUCK_MODEL: 'PUCK_MODEL',
  GOAL_MODEL: 'GOAL_MODEL',
  ARENA_MODEL: 'ARENA_MODEL',
  VFX_THEME: 'VFX_THEME',
  SFX_THEME: 'SFX_THEME',
} as const;

/** Individual, stable asset-slot identifier. */
export type AssetSlot = (typeof ASSET_SLOTS)[keyof typeof ASSET_SLOTS];

/**
 * The prototype's presentation contract.  Models should be authored to this
 * convention before they replace the procedural visual factory output.
 *
 * - Units are metres.
 * - +Y is up; table play plane is Y = 0 in table-local space.
 * - +X is table-right when looking from the player half towards the AI.
 * - +Z points from the player goal to the AI goal.
 * - Table anchor is the centre of the playable surface at (0, 0, 0).
 * - Mallet anchor is the centre of its collision footprint on the play plane.
 * - Puck anchor is its physics-body centre, not its bottom face.
 * - Goal anchor is the midpoint of its goal line on the play plane.  Its
 *   portal plane faces along the Z axis.
 * - Arena anchor is the centre of the duel hall floor.
 *
 * Collision is intentionally absent from this contract.  Rapier shapes and
 * game-rule dimensions remain data owned by the physics/rules layers.
 */
export const ASSET_COORDINATE_CONVENTION = {
  unit: 'metre',
  upAxis: '+Y',
  playerToAiAxis: '+Z',
  tableAnchor: 'playable-surface-centre',
  malletAnchor: 'collision-footprint-centre-on-play-plane',
  puckAnchor: 'physics-body-centre',
  goalAnchor: 'goal-line-midpoint-on-play-plane',
  arenaAnchor: 'duel-hall-floor-centre',
} as const;

export interface AssetSlotMetadata {
  /** Slot name shared by rendering implementations. */
  slot: AssetSlot;
  /** Which authored anchor must match the runtime transform. */
  anchor: string;
  /** Free-form hints for a future model importer or Unity asset pipeline. */
  notes: string;
}

/**
 * A compact, serialisable manifest starter for an eventual asset pipeline.
 * It is intentionally references-only: no loader or game rule imports live
 * here, so WebXR and Unity can bind their own asset systems independently.
 */
export const DEFAULT_ASSET_MANIFEST: Record<AssetSlot, AssetSlotMetadata> = {
  TABLE_MODEL: {
    slot: ASSET_SLOTS.TABLE_MODEL,
    anchor: ASSET_COORDINATE_CONVENTION.tableAnchor,
    notes: 'Scale to the playable surface dimensions; rails and legs are visual only.',
  },
  MALLET_MODEL: {
    slot: ASSET_SLOTS.MALLET_MODEL,
    anchor: ASSET_COORDINATE_CONVENTION.malletAnchor,
    notes: 'Keep the strike rim centred on XZ; do not bake in a collision radius.',
  },
  PUCK_MODEL: {
    slot: ASSET_SLOTS.PUCK_MODEL,
    anchor: ASSET_COORDINATE_CONVENTION.puckAnchor,
    notes: 'Centred on the dynamic body centre; visual radius can differ from physics radius.',
  },
  GOAL_MODEL: {
    slot: ASSET_SLOTS.GOAL_MODEL,
    anchor: ASSET_COORDINATE_CONVENTION.goalAnchor,
    notes: 'Portal opening must straddle the goal line and face the Z axis.',
  },
  ARENA_MODEL: {
    slot: ASSET_SLOTS.ARENA_MODEL,
    anchor: ASSET_COORDINATE_CONVENTION.arenaAnchor,
    notes: 'Do not include a gameplay collision table inside the arena model.',
  },
  VFX_THEME: {
    slot: ASSET_SLOTS.VFX_THEME,
    anchor: 'effect-emitter-transform',
    notes: 'Use the same world-space metre scale and avoid rule-dependent timings.',
  },
  SFX_THEME: {
    slot: ASSET_SLOTS.SFX_THEME,
    anchor: 'event-emitter-transform',
    notes: 'Bind sounds to gameplay events, not directly to presentation meshes.',
  },
};

/** Attach a stable presentation slot marker to a Three.js Object3D userData. */
export function markAssetSlot<T extends { userData: Record<string, unknown> }>(
  object: T,
  slot: AssetSlot,
): T {
  object.userData.assetSlot = slot;
  object.userData.renderOnly = true;
  return object;
}
