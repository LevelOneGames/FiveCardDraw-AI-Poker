using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캔버스에서 사용하는 베팅 칩 한 개의 표시를 담당합니다.
/// 이동 애니메이션과 풀 관리는 PokerChipBetAnimator/PokerChipPool이 담당합니다.
/// </summary>
[DisallowMultipleComponent]
public class PokerChipVisual : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("칩 이미지를 표시할 Image입니다. 비워두면 같은 오브젝트에서 자동으로 찾습니다.")]
    public Image chipImage;

    [Tooltip("칩 단위 문구를 표시할 선택형 Text입니다. 사용하지 않으면 비워둡니다.")]
    public Text valueText;

    private RectTransform cachedRectTransform;
    private Sprite prefabDefaultSprite;
    private Color denominationColor = Color.white;
    private int spawnVersion;

    public RectTransform RectTransform
    {
        get
        {
            if (cachedRectTransform == null)
            {
                cachedRectTransform = transform as RectTransform;
            }

            return cachedRectTransform;
        }
    }

    public int SpawnVersion
    {
        get { return spawnVersion; }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (cachedRectTransform == null)
        {
            cachedRectTransform = transform as RectTransform;
        }

        if (chipImage == null)
        {
            chipImage = GetComponent<Image>();
        }

        if (prefabDefaultSprite == null && chipImage != null)
        {
            prefabDefaultSprite = chipImage.sprite;
        }

        if (valueText == null)
        {
            valueText = GetComponentInChildren<Text>(true);
        }
    }

    /// <summary>
    /// 풀에서 꺼낸 칩에 단위별 외형을 적용합니다.
    /// 반환된 버전은 애니메이션 중 재사용 충돌을 방지하는 데 사용합니다.
    /// </summary>
    public int Prepare(
        Sprite sprite,
        Color color,
        string label,
        Vector2 size,
        float scale)
    {
        ResolveReferences();
        spawnVersion++;

        denominationColor = color;

        if (chipImage != null)
        {
            // 단위별 Sprite가 있으면 그것을 사용하고, 비어 있으면 프리팹 기본 Sprite로 복원합니다.
            // 풀링된 칩에 이전 단위의 이미지가 남는 현상을 방지합니다.
            chipImage.sprite = sprite != null
                ? sprite
                : prefabDefaultSprite;

            chipImage.color = denominationColor;
            chipImage.raycastTarget = false;
        }

        if (valueText != null)
        {
            valueText.text = label ?? string.Empty;
            valueText.raycastTarget = false;
        }

        if (cachedRectTransform != null)
        {
            if (size.x > 0f && size.y > 0f)
            {
                cachedRectTransform.sizeDelta = size;
            }

            cachedRectTransform.localScale =
                Vector3.one * Mathf.Max(0.01f, scale);

            cachedRectTransform.localRotation = Quaternion.identity;
        }

        gameObject.SetActive(true);
        return spawnVersion;
    }

    public bool IsSpawnVersion(int version)
    {
        return spawnVersion == version && gameObject.activeSelf;
    }

    /// <summary>
    /// 오래된 칩일수록 어둡게 보이도록 원래 단위 색상에 밝기를 곱합니다.
    /// </summary>
    public void SetDepthBrightness(float brightness)
    {
        ResolveReferences();

        brightness = Mathf.Clamp01(brightness);

        if (chipImage != null)
        {
            chipImage.color = new Color(
                denominationColor.r * brightness,
                denominationColor.g * brightness,
                denominationColor.b * brightness,
                denominationColor.a
            );
        }

        if (valueText != null)
        {
            Color textColor = valueText.color;
            textColor.a = Mathf.Clamp01(0.45f + brightness * 0.55f);
            valueText.color = textColor;
        }
    }

    /// <summary>
    /// 풀로 돌아갈 때 현재 진행 중인 애니메이션을 무효화합니다.
    /// </summary>
    public void InvalidateAndHide()
    {
        spawnVersion++;

        if (cachedRectTransform != null)
        {
            cachedRectTransform.localRotation = Quaternion.identity;
            cachedRectTransform.localScale = Vector3.one;
        }

        gameObject.SetActive(false);
    }
}