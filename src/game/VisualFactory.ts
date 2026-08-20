import * as THREE from 'three';
import { ASSET_SLOTS, markAssetSlot } from './AssetSlots';

/**
 * Procedural, render-only presentation for the Arcane Puck prototype.
 *
 * Coordinate/anchor convention:
 * - metres, Y-up, player -> AI is +Z;
 * - the table play plane is local Y=0 and its origin is surface centre;
 * - mallets anchor at their collision footprint centre on the play plane;
 * - pucks anchor at their physics-body centre;
 * - goals anchor at their goal-line midpoint on the play plane.
 *
 * No method in this module creates Rapier bodies/colliders or owns gameplay
 * state.  Physics should synchronise an Object3D's transform into these
 * visuals, and models can later replace any group marked with `assetSlot`.
 */

const TAU = Math.PI * 2;

const COLORS = {
  wood: 0x351d16,
  woodWarm: 0x704126,
  woodLight: 0xa66a36,
  metal: 0x33404d,
  gold: 0xe6bd62,
  cyan: 0x46e8ff,
  violet: 0xa579ff,
  violetDeep: 0x351d7a,
  ember: 0xffad5a,
  hallStone: 0x172131,
  floorStone: 0x101520,
} as const;

function standardMaterial(
  color: THREE.ColorRepresentation,
  metalness = 0,
  roughness = 0.7,
): THREE.MeshStandardMaterial {
  return new THREE.MeshStandardMaterial({ color, metalness, roughness });
}

function emissiveMaterial(
  color: THREE.ColorRepresentation,
  intensity = 1.5,
  transparent = false,
): THREE.MeshStandardMaterial {
  return new THREE.MeshStandardMaterial({
    color,
    emissive: color,
    emissiveIntensity: intensity,
    transparent,
    opacity: transparent ? 0.72 : 1,
    depthWrite: !transparent,
  });
}

function shadowMesh<T extends THREE.Mesh>(mesh: T, cast = true, receive = true): T {
  mesh.castShadow = cast;
  mesh.receiveShadow = receive;
  return mesh;
}

function addMesh(
  parent: THREE.Object3D,
  geometry: THREE.BufferGeometry,
  material: THREE.Material | THREE.Material[],
  position?: THREE.Vector3,
): THREE.Mesh {
  const mesh = shadowMesh(new THREE.Mesh(geometry, material));
  if (position) mesh.position.copy(position);
  parent.add(mesh);
  return mesh;
}

export interface TableVisualOptions {
  /** Gameplay playfield width (X), in metres. */
  width?: number;
  /** Gameplay playfield length (Z), in metres. */
  length?: number;
  /** Visual-only opening width; physics remains elsewhere. */
  goalWidth?: number;
  /** Height of render-only rails above the play plane. */
  railHeight?: number;
  /** Decorative table depth below the play plane. */
  bodyDepth?: number;
}

export interface GoalVisualOptions {
  /** Player goal is -Z; AI goal is +Z. */
  side: 'player' | 'ai';
  width?: number;
  height?: number;
  /** Accent colour for a team/goal. */
  color?: THREE.ColorRepresentation;
}

export interface MalletVisualOptions {
  radius?: number;
  /** Full height of the striker base. */
  height?: number;
  color?: THREE.ColorRepresentation;
  /** Used only for presentation colour treatment. */
  owner?: 'player' | 'ai';
}

export interface PuckVisualOptions {
  radius?: number;
  height?: number;
  color?: THREE.ColorRepresentation;
}

export interface WizardVisualOptions {
  color?: THREE.ColorRepresentation;
  staffColor?: THREE.ColorRepresentation;
  scale?: number;
}

export interface DuelHallVisualOptions {
  width?: number;
  length?: number;
  floorY?: number;
  wallHeight?: number;
}

export interface ArcaneParticleFieldOptions {
  count?: number;
  /** Half extents used for softly drifting ambient particles. */
  bounds?: THREE.Vector3;
  color?: THREE.ColorRepresentation;
  seed?: number;
}

/** A deliberately small subset of the shared gameplay config consumed by rendering. */
export interface VisualGameConfig {
  table?: {
    width?: number;
    length?: number;
    /** World-space Y of the physical play plane. */
    height?: number;
    /** Visual rail height above the play plane. */
    wallHeight?: number;
    goalWidth?: number;
    goalHeight?: number;
  };
  puck?: {
    radius?: number;
    height?: number;
  };
  mallet?: {
    radius?: number;
    height?: number;
  };
}

/** Position-like values accepted without coupling visuals to Rapier's vector types. */
export interface VisualPosition {
  x: number;
  y: number;
  z: number;
}

/**
 * Optional presentation signals supplied by the game coordinator.  This has
 * an index signature on purpose: rules may evolve independently without
 * forcing rendering to import a gameplay-state type.
 */
export interface VisualUpdateState {
  elapsedSeconds?: number;
  puckSpeed?: number;
  playerCharge?: number;
  aiActivity?: number;
  hitPosition?: VisualPosition;
  scoreSide?: 'player' | 'ai';
  [key: string]: unknown;
}

export interface MalletVisual extends THREE.Group {
  /** 0..1 visual charge set by input/gameplay. */
  userData: THREE.Group['userData'] & { charge: number };
}

export interface PuckVisual extends THREE.Group {
  userData: THREE.Group['userData'] & { speed: number; spin: number };
}

export interface GoalVisual extends THREE.Group {
  userData: THREE.Group['userData'] & { portalPhase: number; scoredPulse: number };
}

export interface WizardVisual extends THREE.Group {
  userData: THREE.Group['userData'] & { activity: number; baseY: number };
}

/**
 * A small deterministic particle field. It contains no physics: use `burst`
 * with game-event positions and call `update` from the render loop.
 */
export class ArcaneParticleField extends THREE.Points<THREE.BufferGeometry, THREE.PointsMaterial> {
  private readonly positions: Float32Array;
  private readonly velocities: Float32Array;
  private readonly life: Float32Array;
  private readonly maxLife: Float32Array;
  private readonly bounds: THREE.Vector3;
  private randomState: number;

  constructor(options: ArcaneParticleFieldOptions = {}) {
    const count = options.count ?? 96;
    const bounds = options.bounds?.clone() ?? new THREE.Vector3(3.3, 2.3, 4.7);
    const geometry = new THREE.BufferGeometry();
    const positions = new Float32Array(count * 3);
    const colors = new Float32Array(count * 3);
    const velocities = new Float32Array(count * 3);
    const life = new Float32Array(count);
    const maxLife = new Float32Array(count);
    const color = new THREE.Color(options.color ?? COLORS.cyan);
    const seed = (options.seed ?? 0x4f1bbcdc) >>> 0;
    let randomState = seed || 1;
    const next = (): number => {
      randomState = (randomState * 1664525 + 1013904223) >>> 0;
      return randomState / 0x100000000;
    };

    for (let index = 0; index < count; index += 1) {
      const p = index * 3;
      positions[p] = (next() * 2 - 1) * bounds.x;
      positions[p + 1] = next() * bounds.y + 0.1;
      positions[p + 2] = (next() * 2 - 1) * bounds.z;
      velocities[p] = (next() - 0.5) * 0.05;
      velocities[p + 1] = 0.025 + next() * 0.08;
      velocities[p + 2] = (next() - 0.5) * 0.05;
      life[index] = next() * 4;
      maxLife[index] = 3 + next() * 4;
      colors[p] = color.r;
      colors[p + 1] = color.g;
      colors[p + 2] = color.b;
    }

    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
    const material = new THREE.PointsMaterial({
      size: 0.036,
      sizeAttenuation: true,
      vertexColors: true,
      transparent: true,
      opacity: 0.82,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
    });
    super(geometry, material);
    this.name = 'arcane-particle-field';
    this.positions = positions;
    this.velocities = velocities;
    this.life = life;
    this.maxLife = maxLife;
    this.bounds = bounds;
    this.randomState = randomState;
    this.frustumCulled = false;
    this.userData.renderOnly = true;
    this.userData.assetSlot = ASSET_SLOTS.VFX_THEME;
  }

  /** Emits a short additive sparkle burst from a world/local-space point. */
  burst(origin: THREE.Vector3, strength = 1, amount = 18): void {
    const count = this.life.length;
    const requested = Math.max(1, Math.min(count, Math.floor(amount)));
    for (let step = 0; step < requested; step += 1) {
      const index = (Math.floor(this.nextRandom() * count) + step) % count;
      const p = index * 3;
      const theta = this.nextRandom() * TAU;
      const vertical = 0.15 + this.nextRandom() * 0.85;
      const speed = (0.45 + this.nextRandom() * 1.5) * strength;
      this.positions[p] = origin.x;
      this.positions[p + 1] = origin.y;
      this.positions[p + 2] = origin.z;
      this.velocities[p] = Math.cos(theta) * speed;
      this.velocities[p + 1] = vertical * speed;
      this.velocities[p + 2] = Math.sin(theta) * speed;
      this.life[index] = 0;
      this.maxLife[index] = 0.32 + this.nextRandom() * 0.55;
    }
  }

  update(deltaSeconds: number): void {
    const delta = Math.min(Math.max(deltaSeconds, 0), 0.05);
    for (let index = 0; index < this.life.length; index += 1) {
      const p = index * 3;
      this.life[index] += delta;
      if (this.life[index] >= this.maxLife[index]) {
        this.recycle(index);
        continue;
      }
      this.positions[p] += this.velocities[p] * delta;
      this.positions[p + 1] += this.velocities[p + 1] * delta;
      this.positions[p + 2] += this.velocities[p + 2] * delta;
      this.velocities[p + 1] += 0.025 * delta;
    }
    (this.geometry.getAttribute('position') as THREE.BufferAttribute).needsUpdate = true;
  }

  dispose(): void {
    this.geometry.dispose();
    this.material.dispose();
  }

  private recycle(index: number): void {
    const p = index * 3;
    this.positions[p] = (this.nextRandom() * 2 - 1) * this.bounds.x;
    this.positions[p + 1] = this.nextRandom() * this.bounds.y + 0.08;
    this.positions[p + 2] = (this.nextRandom() * 2 - 1) * this.bounds.z;
    this.velocities[p] = (this.nextRandom() - 0.5) * 0.05;
    this.velocities[p + 1] = 0.025 + this.nextRandom() * 0.08;
    this.velocities[p + 2] = (this.nextRandom() - 0.5) * 0.05;
    this.life[index] = 0;
    this.maxLife[index] = 3 + this.nextRandom() * 4;
  }

  private nextRandom(): number {
    this.randomState = (this.randomState * 1664525 + 1013904223) >>> 0;
    return this.randomState / 0x100000000;
  }
}

/** Create the render-only enchanted wood hockey table; its local play plane is Y=0. */
export function createArcaneTableVisual(options: TableVisualOptions = {}): THREE.Group {
  const width = options.width ?? 2.35;
  const length = options.length ?? 3.9;
  const goalWidth = Math.min(options.goalWidth ?? 0.88, width * 0.72);
  const railHeight = options.railHeight ?? 0.14;
  const bodyDepth = options.bodyDepth ?? 0.28;
  const railThickness = 0.075;
  const group = markAssetSlot(new THREE.Group(), ASSET_SLOTS.TABLE_MODEL);
  group.name = 'arcane-table-visual';
  group.userData.renderOnly = true;
  group.userData.playPlaneY = 0;

  const wood = standardMaterial(COLORS.wood, 0.02, 0.55);
  const woodWarm = standardMaterial(COLORS.woodWarm, 0.03, 0.5);
  const metal = standardMaterial(COLORS.metal, 0.78, 0.28);
  const gold = standardMaterial(COLORS.gold, 0.72, 0.23);
  const cyanGlow = emissiveMaterial(COLORS.cyan, 1.8, true);
  const violetGlow = emissiveMaterial(COLORS.violet, 1.25, true);

  // Board body: tabletop is deliberately a visual shell below the physics plane.
  addMesh(group, new THREE.BoxGeometry(width + 0.22, bodyDepth, length + 0.22), wood, new THREE.Vector3(0, -bodyDepth / 2 - 0.025, 0));
  addMesh(group, new THREE.BoxGeometry(width + 0.29, 0.075, length + 0.29), metal, new THREE.Vector3(0, -bodyDepth - 0.06, 0));
  addMesh(group, new THREE.BoxGeometry(width, 0.035, length), woodWarm, new THREE.Vector3(0, -0.035 / 2, 0));

  // Arcane inlay across the field, lifted enough to avoid z-fighting.
  const centreRing = shadowMesh(new THREE.Mesh(new THREE.TorusGeometry(0.31, 0.012, 8, 32), cyanGlow), false, false);
  centreRing.rotation.x = Math.PI / 2;
  centreRing.position.y = 0.009;
  group.add(centreRing);
  const centreGlyph = shadowMesh(new THREE.Mesh(new THREE.RingGeometry(0.1, 0.105, 6), violetGlow), false, false);
  centreGlyph.rotation.x = -Math.PI / 2;
  centreGlyph.position.y = 0.011;
  group.add(centreGlyph);
  const centreLine = addMesh(group, new THREE.BoxGeometry(width - 0.18, 0.008, 0.026), cyanGlow, new THREE.Vector3(0, 0.008, 0));
  centreLine.castShadow = false;

  // Rails are visual-only and intentionally use the same dimensions passed to physics only by caller configuration.
  const sideRailGeometry = new THREE.BoxGeometry(railThickness, railHeight, length + railThickness * 2);
  for (const x of [-width / 2 - railThickness / 2, width / 2 + railThickness / 2]) {
    addMesh(group, sideRailGeometry, metal, new THREE.Vector3(x, railHeight / 2, 0));
    const trim = addMesh(group, new THREE.BoxGeometry(0.018, railHeight * 0.52, length - 0.08), gold, new THREE.Vector3(x + Math.sign(x) * -0.043, railHeight * 0.59, 0));
    trim.castShadow = false;
  }
  const endRailLength = (width - goalWidth) / 2;
  for (const z of [-length / 2 - railThickness / 2, length / 2 + railThickness / 2]) {
    for (const x of [-(goalWidth + endRailLength) / 2, (goalWidth + endRailLength) / 2]) {
      addMesh(group, new THREE.BoxGeometry(endRailLength, railHeight, railThickness), metal, new THREE.Vector3(x, railHeight / 2, z));
    }
  }

  // Ornamental feet and corner finials live below/outside the playfield.
  const legGeometry = new THREE.CylinderGeometry(0.12, 0.15, 0.72, 8);
  for (const x of [-width * 0.43, width * 0.43]) {
    for (const z of [-length * 0.43, length * 0.43]) {
      addMesh(group, legGeometry, wood, new THREE.Vector3(x, -bodyDepth - 0.43, z));
      addMesh(group, new THREE.CylinderGeometry(0.155, 0.155, 0.05, 12), gold, new THREE.Vector3(x, -bodyDepth - 0.81, z));
      const finial = shadowMesh(new THREE.Mesh(new THREE.OctahedronGeometry(0.075, 0), cyanGlow), false, false);
      finial.position.set(Math.sign(x) * (width / 2 + 0.07), railHeight + 0.055, Math.sign(z) * (length / 2 + 0.07));
      group.add(finial);
    }
  }

  return group;
}

/** Create a portal frame at a goal-line anchor. Its local portal plane is X/Y. */
export function createPortalGoalVisual(options: GoalVisualOptions): GoalVisual {
  const width = options.width ?? 0.88;
  const height = options.height ?? 0.42;
  const color = options.color ?? (options.side === 'player' ? COLORS.cyan : COLORS.violet);
  const group = markAssetSlot(new THREE.Group(), ASSET_SLOTS.GOAL_MODEL) as GoalVisual;
  group.name = `${options.side}-portal-goal-visual`;
  group.userData.portalPhase = options.side === 'player' ? 0 : Math.PI;
  group.userData.scoredPulse = 0;
  group.userData.side = options.side;

  const metal = standardMaterial(COLORS.metal, 0.82, 0.24);
  const glow = emissiveMaterial(color, 1.9, true);
  const darkPortal = new THREE.MeshStandardMaterial({
    color: new THREE.Color(color).multiplyScalar(0.22),
    emissive: color,
    emissiveIntensity: 0.55,
    transparent: true,
    opacity: 0.75,
    side: THREE.DoubleSide,
    depthWrite: false,
  });

  // A flattened portal ellipse gives the opening a magical, not literal-net, silhouette.
  const portal = shadowMesh(new THREE.Mesh(new THREE.CircleGeometry(0.5, 40), darkPortal), false, false);
  portal.name = 'portal-surface';
  portal.scale.set(width, height * 2, 1);
  portal.position.y = height * 0.5;
  group.add(portal);

  const frame = shadowMesh(new THREE.Mesh(new THREE.TorusGeometry(0.5, 0.042, 10, 40), metal), true, true);
  frame.name = 'portal-frame';
  frame.scale.set(width, height * 2, 1);
  frame.position.y = height * 0.5;
  group.add(frame);
  const innerRing = shadowMesh(new THREE.Mesh(new THREE.TorusGeometry(0.5, 0.013, 8, 32), glow), false, false);
  innerRing.name = 'portal-glow-ring';
  innerRing.scale.set(width * 0.92, height * 1.84, 1);
  innerRing.userData.baseScaleX = width * 0.92;
  innerRing.userData.baseScaleY = height * 1.84;
  innerRing.position.y = height * 0.5;
  innerRing.position.z = options.side === 'player' ? 0.006 : -0.006;
  group.add(innerRing);

  // Four runic stones visually seat the portal but do not obstruct the physical goal mouth.
  const runeGeometry = new THREE.OctahedronGeometry(0.055, 0);
  for (const x of [-width * 0.47, width * 0.47]) {
    const rune = shadowMesh(new THREE.Mesh(runeGeometry, glow), false, false);
    rune.position.set(x, 0.065, 0);
    group.add(rune);
  }
  return group;
}

/**
 * Create a mallet whose origin matches the bottom centre of its physical
 * footprint.  The caller synchronises this group from the physics/input layer.
 */
export function createRuneMalletVisual(options: MalletVisualOptions = {}): MalletVisual {
  const radius = options.radius ?? 0.14;
  const height = options.height ?? 0.105;
  const color = options.color ?? (options.owner === 'ai' ? COLORS.violet : COLORS.cyan);
  const group = markAssetSlot(new THREE.Group(), ASSET_SLOTS.MALLET_MODEL) as MalletVisual;
  group.name = `${options.owner ?? 'player'}-rune-mallet-visual`;
  group.userData.charge = 0;
  group.userData.owner = options.owner ?? 'player';

  const rim = standardMaterial(COLORS.metal, 0.86, 0.2);
  const grip = standardMaterial(COLORS.woodWarm, 0.05, 0.48);
  const glow = emissiveMaterial(color, 1.4, true);
  const base = addMesh(group, new THREE.CylinderGeometry(radius, radius * 1.04, height, 28), rim, new THREE.Vector3(0, height / 2, 0));
  base.name = 'mallet-strike-rim';
  const baseGlow = shadowMesh(new THREE.Mesh(new THREE.TorusGeometry(radius * 0.73, 0.018, 8, 28), glow), false, false);
  baseGlow.name = 'mallet-charge-ring';
  baseGlow.rotation.x = Math.PI / 2;
  baseGlow.position.y = height + 0.008;
  group.add(baseGlow);
  const head = shadowMesh(new THREE.Mesh(new THREE.CylinderGeometry(radius * 0.62, radius * 0.73, height * 0.38, 24), glow), false, false);
  head.name = 'mallet-charge-head';
  head.position.y = height * 1.12;
  group.add(head);
  const handle = addMesh(group, new THREE.CylinderGeometry(radius * 0.22, radius * 0.28, radius * 2.5, 12), grip, new THREE.Vector3(0, height + radius * 1.23, 0));
  handle.name = 'mallet-handle';
  const pommel = shadowMesh(new THREE.Mesh(new THREE.SphereGeometry(radius * 0.32, 16, 12), glow), false, false);
  pommel.position.y = height + radius * 2.52;
  group.add(pommel);

  return group;
}

/** Set a visual-only 0..1 charge indication on a rune mallet. */
export function setMalletCharge(mallet: MalletVisual, charge: number): void {
  const normalised = THREE.MathUtils.clamp(charge, 0, 1);
  mallet.userData.charge = normalised;
  const ring = mallet.getObjectByName('mallet-charge-ring') as THREE.Mesh | undefined;
  const head = mallet.getObjectByName('mallet-charge-head') as THREE.Mesh | undefined;
  if (ring?.material instanceof THREE.MeshStandardMaterial) {
    ring.material.emissiveIntensity = 0.55 + normalised * 4.2;
    ring.scale.setScalar(1 + normalised * 0.14);
  }
  if (head?.material instanceof THREE.MeshStandardMaterial) {
    head.material.emissiveIntensity = 1.2 + normalised * 1.8;
  }
}

/** Create a puck anchored at the centre of its physics body. */
export function createArcanePuckVisual(options: PuckVisualOptions = {}): PuckVisual {
  const radius = options.radius ?? 0.095;
  const height = options.height ?? 0.052;
  const color = options.color ?? COLORS.ember;
  const group = markAssetSlot(new THREE.Group(), ASSET_SLOTS.PUCK_MODEL) as PuckVisual;
  group.name = 'arcane-puck-visual';
  group.userData.speed = 0;
  group.userData.spin = 0;

  const darkMetal = standardMaterial(COLORS.metal, 0.8, 0.18);
  const glow = emissiveMaterial(color, 2.2, true);
  const disc = addMesh(group, new THREE.CylinderGeometry(radius, radius, height, 30), darkMetal);
  disc.name = 'puck-shell';
  const core = shadowMesh(new THREE.Mesh(new THREE.CylinderGeometry(radius * 0.62, radius * 0.62, height * 1.08, 24), glow), false, false);
  core.name = 'puck-core';
  group.add(core);
  const ring = shadowMesh(new THREE.Mesh(new THREE.TorusGeometry(radius * 0.74, radius * 0.012, 7, 28), glow), false, false);
  ring.name = 'puck-rune-ring';
  ring.rotation.x = Math.PI / 2;
  ring.position.y = height * 0.56;
  group.add(ring);
  const sigil = shadowMesh(new THREE.Mesh(new THREE.CircleGeometry(radius * 0.35, 6), glow), false, false);
  sigil.name = 'puck-sigil';
  sigil.rotation.x = -Math.PI / 2;
  sigil.position.y = height * 0.57;
  group.add(sigil);
  return group;
}

/** Update render-only puck spin and brightness from its current planar speed. */
export function updateArcanePuckVisual(puck: PuckVisual, deltaSeconds: number, planarSpeed: number): void {
  const speed = Math.max(0, planarSpeed);
  puck.userData.speed = speed;
  puck.userData.spin += Math.min(Math.max(deltaSeconds, 0), 0.05) * speed * 11;
  const ring = puck.getObjectByName('puck-rune-ring');
  const sigil = puck.getObjectByName('puck-sigil');
  if (ring) ring.rotation.z = puck.userData.spin;
  if (sigil) sigil.rotation.z = -puck.userData.spin * 0.6;
  const intensity = 1.15 + Math.min(speed * 0.34, 2.4);
  for (const name of ['puck-core', 'puck-rune-ring', 'puck-sigil']) {
    const object = puck.getObjectByName(name) as THREE.Mesh | undefined;
    if (object?.material instanceof THREE.MeshStandardMaterial) object.material.emissiveIntensity = intensity;
  }
}

/** Minimal animated wizard silhouette. Its origin is the ground/contact point below the robe. */
export function createAiWizardVisual(options: WizardVisualOptions = {}): WizardVisual {
  const group = new THREE.Group() as WizardVisual;
  markAssetSlot(group, ASSET_SLOTS.ARENA_MODEL);
  group.name = 'ai-wizard-placeholder-visual';
  group.userData.activity = 0;
  group.userData.baseY = 0;
  group.scale.setScalar(options.scale ?? 1);

  const robe = standardMaterial(options.color ?? COLORS.violetDeep, 0.15, 0.62);
  const trim = standardMaterial(COLORS.gold, 0.72, 0.22);
  const skin = standardMaterial(0xc28a68, 0, 0.75);
  const magic = emissiveMaterial(options.staffColor ?? COLORS.violet, 1.8, true);
  const robeBody = addMesh(group, new THREE.ConeGeometry(0.32, 0.85, 16), robe, new THREE.Vector3(0, 0.425, 0));
  robeBody.name = 'wizard-robe';
  const belt = shadowMesh(new THREE.Mesh(new THREE.TorusGeometry(0.19, 0.028, 6, 20), trim), true, true);
  belt.rotation.x = Math.PI / 2;
  belt.position.y = 0.43;
  group.add(belt);
  const head = addMesh(group, new THREE.SphereGeometry(0.14, 16, 12), skin, new THREE.Vector3(0, 0.95, 0));
  head.name = 'wizard-head';
  const hat = addMesh(group, new THREE.ConeGeometry(0.22, 0.53, 16), robe, new THREE.Vector3(0, 1.26, 0));
  hat.name = 'wizard-hat';
  const hatBrim = shadowMesh(new THREE.Mesh(new THREE.CylinderGeometry(0.27, 0.27, 0.035, 18), trim), true, true);
  hatBrim.position.y = 1.06;
  group.add(hatBrim);
  const staff = addMesh(group, new THREE.CylinderGeometry(0.022, 0.027, 1.1, 8), standardMaterial(COLORS.woodLight, 0, 0.52), new THREE.Vector3(0.38, 0.58, 0));
  staff.name = 'wizard-staff';
  staff.rotation.z = -0.13;
  const orb = shadowMesh(new THREE.Mesh(new THREE.SphereGeometry(0.08, 14, 10), magic), false, false);
  orb.name = 'wizard-orb';
  orb.position.set(0.45, 1.15, 0);
  group.add(orb);
  return group;
}

/** Presentational idle/casting animation; `activity` normally follows AI intent in [0,1]. */
export function updateAiWizardVisual(wizard: WizardVisual, elapsedSeconds: number, activity = wizard.userData.activity): void {
  const active = THREE.MathUtils.clamp(activity, 0, 1);
  wizard.userData.activity = active;
  const bob = Math.sin(elapsedSeconds * (1.7 + active * 1.3)) * (0.018 + active * 0.025);
  wizard.position.y = wizard.userData.baseY + bob;
  const orb = wizard.getObjectByName('wizard-orb');
  if (orb) {
    orb.position.y = 1.15 + Math.sin(elapsedSeconds * 3.4) * (0.045 + active * 0.045);
    orb.rotation.y = elapsedSeconds * 2.8;
    if (orb instanceof THREE.Mesh && orb.material instanceof THREE.MeshStandardMaterial) {
      orb.material.emissiveIntensity = 1.25 + active * 1.45 + Math.sin(elapsedSeconds * 5) * 0.2;
    }
  }
  const staff = wizard.getObjectByName('wizard-staff');
  if (staff) staff.rotation.z = -0.13 - Math.sin(elapsedSeconds * 2.2) * active * 0.15;
}

/** Create a render-only magical academy duel hall centred on the floor anchor. */
export function createDuelHallVisual(options: DuelHallVisualOptions = {}): THREE.Group {
  const width = options.width ?? 9;
  const length = options.length ?? 11;
  const floorY = options.floorY ?? -1.12;
  const wallHeight = options.wallHeight ?? 5.6;
  const group = markAssetSlot(new THREE.Group(), ASSET_SLOTS.ARENA_MODEL);
  group.name = 'arcane-academy-duel-hall-visual';
  group.userData.floorY = floorY;

  const floor = standardMaterial(COLORS.floorStone, 0.18, 0.86);
  const wall = standardMaterial(COLORS.hallStone, 0.22, 0.8);
  const stone = standardMaterial(0x263447, 0.18, 0.72);
  const gold = standardMaterial(COLORS.gold, 0.64, 0.27);
  const runeGlow = emissiveMaterial(COLORS.cyan, 1.05, true);
  const violetGlow = emissiveMaterial(COLORS.violet, 1.25, true);

  addMesh(group, new THREE.BoxGeometry(width, 0.12, length), floor, new THREE.Vector3(0, floorY - 0.06, 0));
  const floorRune = shadowMesh(new THREE.Mesh(new THREE.RingGeometry(2.3, 2.34, 64), runeGlow), false, false);
  floorRune.rotation.x = -Math.PI / 2;
  floorRune.position.y = floorY + 0.004;
  group.add(floorRune);
  const innerRune = shadowMesh(new THREE.Mesh(new THREE.RingGeometry(1.12, 1.14, 6), violetGlow), false, false);
  innerRune.rotation.x = -Math.PI / 2;
  innerRune.position.y = floorY + 0.006;
  group.add(innerRune);

  // Low background walls provide scale without producing a closed, uncomfortable VR space.
  addMesh(group, new THREE.BoxGeometry(width, wallHeight, 0.22), wall, new THREE.Vector3(0, floorY + wallHeight / 2, -length / 2));
  addMesh(group, new THREE.BoxGeometry(width, wallHeight, 0.22), wall, new THREE.Vector3(0, floorY + wallHeight / 2, length / 2));

  const pillarGeometry = new THREE.CylinderGeometry(0.24, 0.31, wallHeight, 10);
  const capGeometry = new THREE.CylinderGeometry(0.38, 0.38, 0.1, 10);
  for (const x of [-width / 2 + 0.4, width / 2 - 0.4]) {
    for (const z of [-length / 2 + 0.55, -length / 6, length / 6, length / 2 - 0.55]) {
      addMesh(group, pillarGeometry, stone, new THREE.Vector3(x, floorY + wallHeight / 2, z));
      addMesh(group, capGeometry, gold, new THREE.Vector3(x, floorY + wallHeight - 0.06, z));
      const brazier = shadowMesh(new THREE.Mesh(new THREE.SphereGeometry(0.09, 12, 8), z < 0 ? runeGlow : violetGlow), false, false);
      brazier.position.set(x * 0.94, floorY + 2.15, z);
      group.add(brazier);
    }
  }

  // Floating academy banners are intentionally geometry only, ready for themed assets later.
  const bannerMaterial = standardMaterial(COLORS.violetDeep, 0.1, 0.7);
  for (const x of [-width * 0.29, width * 0.29]) {
    const banner = addMesh(group, new THREE.PlaneGeometry(0.8, 1.55), bannerMaterial, new THREE.Vector3(x, floorY + 3.5, -length / 2 + 0.13));
    banner.rotation.y = Math.PI;
    const crest = shadowMesh(new THREE.Mesh(new THREE.CircleGeometry(0.14, 6), gold), false, false);
    crest.position.set(x, floorY + 3.55, -length / 2 + 0.115);
    crest.rotation.y = Math.PI;
    group.add(crest);
  }
  return group;
}

/** Animate a portal without coupling it to scoring/rules. Call `pulsePortalGoal` on a score. */
export function updatePortalGoalVisual(goal: GoalVisual, elapsedSeconds: number, deltaSeconds: number): void {
  const delta = Math.min(Math.max(deltaSeconds, 0), 0.05);
  goal.userData.portalPhase += delta * 0.85;
  goal.userData.scoredPulse = Math.max(0, goal.userData.scoredPulse - delta * 1.75);
  const pulse = goal.userData.scoredPulse;
  const glowRing = goal.getObjectByName('portal-glow-ring');
  const portal = goal.getObjectByName('portal-surface');
  if (glowRing) {
    glowRing.rotation.z = goal.userData.portalPhase;
    const scale = 1 + Math.sin(elapsedSeconds * 2.6 + goal.userData.portalPhase) * 0.022 + pulse * 0.16;
    const width = (glowRing.userData.baseScaleX as number | undefined) ?? (glowRing.scale.x || 1);
    const height = (glowRing.userData.baseScaleY as number | undefined) ?? (glowRing.scale.y || 1);
    glowRing.userData.baseScaleX = width;
    glowRing.userData.baseScaleY = height;
    glowRing.scale.set(width * scale, height * scale, 1);
    if (glowRing instanceof THREE.Mesh && glowRing.material instanceof THREE.MeshStandardMaterial) {
      glowRing.material.emissiveIntensity = 1.45 + pulse * 4.2;
    }
  }
  if (portal instanceof THREE.Mesh && portal.material instanceof THREE.MeshStandardMaterial) {
    portal.material.emissiveIntensity = 0.45 + Math.sin(elapsedSeconds * 2.1) * 0.1 + pulse * 1.8;
    portal.material.opacity = 0.64 + pulse * 0.25;
  }
}

/** Trigger a short portal score bloom. This is presentation-only. */
export function pulsePortalGoal(goal: GoalVisual): void {
  goal.userData.scoredPulse = 1;
}

/**
 * High-level factory namespace for consumers that prefer a single dependency.
 * The named exports above remain preferable for tree-shaking and testing.
 */
export const VisualFactory = {
  createDuelHall: createDuelHallVisual,
  createTable: createArcaneTableVisual,
  createGoal: createPortalGoalVisual,
  createMallet: createRuneMalletVisual,
  createPuck: createArcanePuckVisual,
  createAiWizard: createAiWizardVisual,
  createParticles: (options?: ArcaneParticleFieldOptions) => new ArcaneParticleField(options),
  updatePuck: updateArcanePuckVisual,
  updateMalletCharge: setMalletCharge,
  updateGoal: updatePortalGoalVisual,
  pulseGoal: pulsePortalGoal,
  updateAiWizard: updateAiWizardVisual,
};
