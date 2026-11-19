using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Marks an interactable that stands in for another one (a crate's body opening its lid). Small things in front of
    /// or inside it (the fuses in the crate) win over it when both are near the crosshair.
    /// </summary>
    public interface IProxyInteractable { }

    /// <summary>
    /// What the player is pointing at, for the desktop crosshair and VR controller rays. An exact hit on an interactable
    /// wins; otherwise a thin sweep around the ray picks the interactable closest to it (so small things like fuses and
    /// tin lids don't need pixel-perfect aim), but never anything behind the first solid surface the ray hits.
    /// </summary>
    public static class InteractionPicker
    {
        public const float DefaultRadius = 0.06f;
        static readonly RaycastHit[] s_Hits = new RaycastHit[48];
        static readonly RaycastHit[] s_Los = new RaycastHit[16];

        /// <param name="ignore">The player's own rig (its CharacterController / colliders are never hit). Empty = only
        /// CharacterControllers are skipped.</param>
        public static IInteractable Pick(Ray ray, float reach, float radius = DefaultRadius, Transform ignore = null)
        {
            IInteractable exact = null;
            float blocker = reach;
            float margin = 0.12f;
            int n = Physics.RaycastNonAlloc(ray, s_Hits, reach, ~0, QueryTriggerInteraction.Ignore);
            int first = -1;
            for (int i = 0; i < n; i++)
            {
                if (Ignored(s_Hits[i].collider, ignore)) continue;
                if (first < 0 || s_Hits[i].distance < s_Hits[first].distance) first = i;
            }
            if (first >= 0)
            {
                var hit = s_Hits[first];
                exact = hit.collider.GetComponentInParent<IInteractable>();
                if (exact != null && !(exact is IProxyInteractable)) return exact;
                blocker = hit.distance;
                if (exact != null) margin = 0.35f;            // looking at a container: things inside it still count
            }
            if (radius <= 0f) return exact;
            if (exact is IProxyInteractable) radius = Mathf.Max(radius, 0.09f);   // inside a container: small things, be generous

            IInteractable best = exact;
            float bestScore = exact != null ? 0.05f : float.MaxValue;
            n = Physics.SphereCastNonAlloc(ray, radius, s_Hits, reach, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = s_Hits[i];
                if (h.distance <= 0f || h.distance > blocker + margin) continue;     // started inside it / behind a wall
                if (Ignored(h.collider, ignore)) continue;
                var it = h.collider.GetComponentInParent<IInteractable>();
                if (it == null) continue;
                if (!ReferenceEquals(it, exact) && Occluded(ray.origin, h, it, exact, ignore)) continue;   // e.g. a fuse inside a closed tin
                float off = Vector3.Cross(ray.direction, h.point - ray.origin).magnitude;
                float score = off + (it is IProxyInteractable ? 0.05f : 0f) + h.distance * 0.005f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = it;
                }
            }
            return best;
        }

        /// <summary>Is there something else between the eye and this near-miss? (Only what you can see is offered.)</summary>
        static bool Occluded(Vector3 eye, RaycastHit target, IInteractable it, IInteractable container, Transform ignore)
        {
            Vector3 d = target.point - eye;
            float dist = d.magnitude - 0.01f;
            if (dist <= 0f) return false;
            int n = Physics.RaycastNonAlloc(new Ray(eye, d / (dist + 0.01f)), s_Los, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_Los[i].collider;
                if (c == target.collider || Ignored(c, ignore)) continue;
                var o = c.GetComponentInParent<IInteractable>();
                if (Same(o, it)) continue;
                // the container we're looking into (its body) doesn't hide what's inside it; a closed lid still does
                if (ReferenceEquals(o, container) && container is InteractProxy p && !(p.target is HingedOpenable lid && !lid.IsOpen)) continue;
                return true;
            }
            return false;
        }

        /// <summary>A fuse sitting in a slot and the slot count as the same thing.</summary>
        static bool Same(IInteractable a, IInteractable b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            if (a is Fuse fa && ReferenceEquals(fa.Socket, b)) return true;
            if (b is Fuse fb && ReferenceEquals(fb.Socket, a)) return true;
            return false;
        }

        /// <summary>The player's own body: looking down, the crosshair ray clips the top of their capsule.</summary>
        static bool Ignored(Collider c, Transform ignore)
        {
            if (c == null) return true;
            if (c is CharacterController) return true;
            return ignore != null && c.transform.IsChildOf(ignore);
        }
    }
}
