import { strict as assert } from 'node:assert';
import { GAME_CONFIG } from '../config/gameConfig';
import { detectGoal, GameRules } from './GameRules';

function liveRules(): GameRules {
  const rules = new GameRules();
  rules.startMatch();
  rules.tick(GAME_CONFIG.match.serveDelaySeconds + 0.01);
  return rules;
}

const rules = liveRules();
for (let score = 0; score < GAME_CONFIG.match.winScore - 1; score += 1) {
  const events = rules.score('player');
  assert.equal(events.some((event) => event.type === 'match-won'), false);
  rules.tick(GAME_CONFIG.match.serveDelaySeconds + 0.01);
}
const finalEvents = rules.score('player');
assert.equal(finalEvents.some((event) => event.type === 'match-won'), true);
assert.equal(rules.snapshot.winner, 'player');

const goldenRules = liveRules();
goldenRules.setScoreForTesting(6, 6);
assert.equal(goldenRules.snapshot.goldenGoal, true);
const goldenEvents = goldenRules.score('ai');
assert.equal(goldenEvents.some((event) => event.type === 'match-won'), true);
assert.equal(goldenRules.snapshot.winner, 'ai');

const halfLength = GAME_CONFIG.table.length / 2;
const puckRadius = GAME_CONFIG.puck.radius;
assert.equal(detectGoal({ x: 0, z: halfLength + puckRadius - 0.001 }), null);
assert.equal(detectGoal({ x: 0, z: halfLength + puckRadius + 0.001 }), 'player');
assert.equal(
  detectGoal({ x: GAME_CONFIG.table.goalWidth / 2 - puckRadius + 0.001, z: halfLength + puckRadius + 0.01 }),
  null,
);

console.log('GameRules tests passed');

