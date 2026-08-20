using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArcanePuck
{
    public sealed class EffectPool : MonoBehaviour
    {
        private readonly Queue<PooledEffect> available = new();
        private readonly List<PooledEffect> all = new();
        private Func<Transform, PooledEffect> factory;
        private int recycleIndex;

        public void Initialize(int capacity, Func<Transform, PooledEffect> createEffect)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (createEffect == null)
            {
                throw new ArgumentNullException(nameof(createEffect));
            }

            factory = createEffect;
            for (var i = 0; i < capacity; i++)
            {
                var effect = factory(transform);
                effect.gameObject.SetActive(false);
                all.Add(effect);
                available.Enqueue(effect);
            }
        }

        public PooledEffect Get()
        {
            PooledEffect effect;
            if (available.Count > 0)
            {
                effect = available.Dequeue();
            }
            else
            {
                effect = all[recycleIndex % all.Count];
                recycleIndex++;
                effect.StopImmediately();
            }

            effect.MarkBorrowed();
            effect.gameObject.SetActive(true);
            return effect;
        }

        public void Return(PooledEffect effect)
        {
            if (effect == null || !effect.TryMarkReturned())
            {
                return;
            }

            effect.gameObject.SetActive(false);
            available.Enqueue(effect);
        }
    }

    public sealed class PooledEffect : MonoBehaviour
    {
        private EffectPool pool;
        private ParticleSystem sparks;
        private Renderer flash;
        private Renderer ring;
        private float duration;
        private float age;
        private Vector3 ringStartScale;
        private Vector3 ringEndScale;
        private bool borrowed;

        public void Initialize(EffectPool owner, ParticleSystem particleSystem, Renderer flashRenderer, Renderer ringRenderer)
        {
            pool = owner;
            sparks = particleSystem;
            flash = flashRenderer;
            ring = ringRenderer;
        }

        public void Play(in HitEvent hitEvent, Color primary, Color secondary)
        {
            transform.position = hitEvent.Point + pool.transform.up * 0.012f;
            var tableLocalDirection = pool.transform.InverseTransformDirection(hitEvent.Direction);
            tableLocalDirection.y = 0f;
            if (tableLocalDirection.sqrMagnitude < 0.0001f)
            {
                tableLocalDirection = Vector3.forward;
            }

            transform.localRotation = Quaternion.LookRotation(tableLocalDirection.normalized, Vector3.up);
            age = 0f;
            duration = hitEvent.Strength switch
            {
                HitStrength.Power => 0.62f,
                HitStrength.Normal => 0.42f,
                _ => 0.24f
            };

            var strengthScale = hitEvent.Strength switch
            {
                HitStrength.Power => 1.55f,
                HitStrength.Normal => 1f,
                _ => 0.55f
            };
            flash.material.color = primary * (hitEvent.Strength == HitStrength.Power ? 1.8f : 1.25f);
            ring.material.color = Color.Lerp(primary, secondary, 0.35f);
            flash.transform.localScale = Vector3.one * (0.1f * strengthScale);
            ringStartScale = new Vector3(0.14f * strengthScale, 0.006f, 0.14f * strengthScale);
            ringEndScale = new Vector3(0.75f * strengthScale, 0.006f, 0.75f * strengthScale);
            ring.transform.localScale = ringStartScale;

            var main = sparks.main;
            main.startColor = new ParticleSystem.MinMaxGradient(primary, secondary);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f * strengthScale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.8f * strengthScale);
            var emission = sparks.emission;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)(hitEvent.Strength == HitStrength.Power ? 34 : hitEvent.Strength == HitStrength.Normal ? 20 : 8))
            });
            sparks.Play(true);
        }

        private void Update()
        {
            age += Time.deltaTime;
            var t = Mathf.Clamp01(age / duration);
            ring.transform.localScale = Vector3.Lerp(ringStartScale, ringEndScale, 1f - (1f - t) * (1f - t));
            flash.transform.localScale *= Mathf.Pow(0.02f, Time.deltaTime / Mathf.Max(duration, 0.01f));
            var ringColor = ring.material.color;
            ringColor.a = 1f - t;
            ring.material.color = ringColor;
            if (age >= duration)
            {
                pool.Return(this);
            }
        }

        public void StopImmediately()
        {
            sparks?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        public void MarkBorrowed()
        {
            borrowed = true;
        }

        public bool TryMarkReturned()
        {
            if (!borrowed)
            {
                return false;
            }

            borrowed = false;
            return true;
        }
    }
}
