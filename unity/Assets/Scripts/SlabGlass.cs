using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A slab's clear case top (see SlabGlass.shader): one material per slab, fed the light the card inside has, mapped
    /// from the card's uv to the case's, so the case and the card always share one light. Under
    /// <see cref="CardFoil.FixedLight"/> the mapping is exact (the harness stays deterministic). With nobody pointing at
    /// it, a foil card's drift is shared; a plain card's slab (or one with no picture) rests at the idle light. Under
    /// Reduced Motion the light rests at <see cref="CardFoil.IdleLight"/> unless the pointer moves it. The reveal drives a
    /// sweep with <see cref="Override"/>, and the sticker flash and perimeter glint.
    /// </summary>
    public sealed class SlabGlass : MonoBehaviour
    {
        private static Shader? _shader;
        private static bool _shaderMissing;

        private Material? _material;
        private RectTransform _card = null!;
        private CardFoil? _foil;
        private Vector2 _caseSize, _cardMin, _cardSize;
        private Vector2 _light = new Vector2(CardFoil.IdleLight.x, CardFoil.IdleLight.y);
        private float _hover;
        private Vector3? _override;
        private Vector4 _sent = new Vector4(float.NaN, 0, 0, 0);

        /// <summary>The slab's size (its label is rebuilt at this size).</summary>
        public SlabView.Size Size { get; set; }

        public static Shader? LoadShader()
        {
            if (_shader == null && !_shaderMissing)
            {
                _shader = Resources.Load<Shader>("SlabGlass");
                _shaderMissing = _shader == null || !_shader.isSupported;
                if (_shaderMissing) Debug.LogWarning("Slab glass shader is missing or unsupported; slabs are drawn without their case top.");
            }
            return _shaderMissing ? null : _shader;
        }

        /// <summary>
        /// Sets the glass up over a case of <paramref name="caseSize"/> holding <paramref name="card"/> (a child of the case,
        /// placed from its centre), with <paramref name="material"/> already given the case's measures (see
        /// <see cref="SlabView"/>) and scuffs from the copy's <paramref name="seed"/>.
        /// </summary>
        public void Init(SlabView.Size size, RawImage image, RectTransform card, Vector2 caseSize, Material material, uint seed)
        {
            Size = size;
            _card = card;
            _caseSize = caseSize;
            _cardSize = card.sizeDelta;
            _cardMin = card.anchoredPosition - _cardSize / 2;
            _material = material;
            _material.SetFloat("_Seed", seed % 9973);
            image.material = _material;
            Update();
        }

        /// <summary>The card's foil (for its drift), once it has one.</summary>
        public void Follow(CardFoil? foil) => _foil = foil;

        /// <summary>Drives the light (case uv xy, hover z) instead of the card's, or gives it back (null).</summary>
        public void Override(Vector3? light)
        {
            _override = light;
            Update();
        }

        /// <summary>The sticker's rect (case px from the centre; zero: none), its flash (1 to 0) and the perimeter glint (-1: off).</summary>
        public void SetSticker(Vector4 rect) => _material?.SetVector("_StickerRect", rect);
        public void SetStickerFlash(float flash) => _material?.SetFloat("_StickerFlash", flash);
        public void SetGlint(float phase) => _material?.SetFloat("_Glint", phase);

        private void Update()
        {
            if (_material == null) return;
            Vector3 light;
            if (_override is Vector3 forced) light = forced;
            else if (CardFoil.FixedLight is Vector3 fixedLight) light = ToCase(fixedLight);
            else
            {
                Vector2 uv = default;
                bool near = _card != null && CardFoil.PointerUv(_card, out uv);
                if (near && _foil != null) light = ToCase(_foil.CurrentLight); // the card is tracking the pointer: the same light
                else if (!near && FxSettings.ReducedMotion) light = ToCase(CardFoil.IdleLight);
                else if (!near && _foil != null && _foil.Foiled) light = ToCase(_foil.CurrentLight); // its drift and glints
                else
                {
                    // No picture, or a plain card: the slab eases to the pointer, or back to the idle light.
                    var target = near ? uv : new Vector2(CardFoil.IdleLight.x, CardFoil.IdleLight.y);
                    float t = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
                    _light = Vector2.Lerp(_light, target, t);
                    _hover = Mathf.Lerp(_hover, near ? 1 : 0, t);
                    if (!near && _hover < 0.002f) _hover = 0;
                    light = ToCase(new Vector3(_light.x, _light.y, _hover));
                }
            }
            var v = new Vector4(light.x, light.y, light.z, 0);
            if (v == _sent) return;
            _sent = v;
            _material.SetVector("_Light", v);
        }

        /// <summary>Card uv to case uv: (cardMin + light x cardSize - caseMin) / caseSize.</summary>
        private Vector3 ToCase(Vector3 cardLight) => new Vector3(
            (_cardMin.x + cardLight.x * _cardSize.x + _caseSize.x / 2) / _caseSize.x,
            (_cardMin.y + cardLight.y * _cardSize.y + _caseSize.y / 2) / _caseSize.y,
            cardLight.z);

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
