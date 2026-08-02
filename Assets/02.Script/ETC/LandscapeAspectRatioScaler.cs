using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가로형 UI의 최소 화면 비율을 16:9로 유지합니다.
///
/// 화면 자체의 실제 가로/세로 비율(Screen.width / Screen.height)을 기준으로 판단합니다.
///
/// - 16:9보다 가로가 길거나 같은 화면
///   CanvasScaler Match Width Or Height = 1
///   Scaler는 부모 전체 영역을 사용합니다.
///
/// - 16:9보다 가로가 짧은 화면
///   CanvasScaler Match Width Or Height = 0
///   Scaler를 16:9 영역으로 줄여 중앙에 배치합니다.
///   남는 공간은 위/아래 여백이 됩니다.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class LandscapeAspectRatioScaler : MonoBehaviour
{
    [Header("Canvas Scaler")]
    [Tooltip("Match Width Or Height를 변경할 CanvasScaler입니다. 반드시 연결하는 것을 권장합니다.")]
    [SerializeField] private CanvasScaler canvasScaler;

    [Header("Target Aspect Ratio")]
    [Min(1f)]
    [SerializeField] private float targetWidth = 16f;

    [Min(1f)]
    [SerializeField] private float targetHeight = 9f;

    [Header("Canvas Scaler Match Values")]
    [Tooltip("16:9보다 가로가 길거나 같을 때 적용합니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float wideScreenMatch = 1f;

    [Tooltip("16:9보다 가로가 짧을 때 적용합니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float narrowScreenMatch = 0f;

    [Header("Debug")]
    [SerializeField] private bool logAspectChanges;

    private RectTransform scalerRect;
    private RectTransform parentRect;

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private Vector2 lastParentSize = new Vector2(-1f, -1f);
    private bool lastWasNarrow;
    private bool hasApplied;
    private bool isApplying;

    private float TargetAspect
    {
        get
        {
            return Mathf.Max(
                0.0001f,
                targetWidth / Mathf.Max(0.0001f, targetHeight)
            );
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ApplyAspectRatio();
    }

    private void Start()
    {
        ApplyAspectRatio();
    }

    private void LateUpdate()
    {
        ResolveReferences();

        if (scalerRect == null || parentRect == null)
        {
            return;
        }

        int screenWidth = Mathf.Max(1, Screen.width);
        int screenHeight = Mathf.Max(1, Screen.height);
        Vector2 parentSize = parentRect.rect.size;

        bool screenChanged =
            screenWidth != lastScreenWidth ||
            screenHeight != lastScreenHeight;

        bool parentChanged =
            Mathf.Abs(parentSize.x - lastParentSize.x) > 0.01f ||
            Mathf.Abs(parentSize.y - lastParentSize.y) > 0.01f;

        bool isNarrow =
            screenWidth / (float)screenHeight < TargetAspect;

        float expectedMatch =
            isNarrow ? narrowScreenMatch : wideScreenMatch;

        bool matchChanged =
            canvasScaler != null &&
            !Mathf.Approximately(
                canvasScaler.matchWidthOrHeight,
                expectedMatch
            );

        if (!hasApplied ||
            screenChanged ||
            parentChanged ||
            isNarrow != lastWasNarrow ||
            matchChanged)
        {
            ApplyAspectRatio();
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isApplying && isActiveAndEnabled)
        {
            ApplyAspectRatio();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        targetWidth = Mathf.Max(1f, targetWidth);
        targetHeight = Mathf.Max(1f, targetHeight);
        wideScreenMatch = Mathf.Clamp01(wideScreenMatch);
        narrowScreenMatch = Mathf.Clamp01(narrowScreenMatch);

        ResolveReferences();

        if (isActiveAndEnabled)
        {
            ApplyAspectRatio();
        }
    }
#endif

    [ContextMenu("Apply Aspect Ratio Now")]
    public void ApplyAspectRatio()
    {
        if (isApplying)
        {
            return;
        }

        ResolveReferences();

        if (scalerRect == null || parentRect == null)
        {
            return;
        }

        int screenWidth = Mathf.Max(1, Screen.width);
        int screenHeight = Mathf.Max(1, Screen.height);

        float screenAspect =
            screenWidth / (float)screenHeight;

        bool isNarrow =
            screenAspect < TargetAspect;

        float targetMatch =
            isNarrow ? narrowScreenMatch : wideScreenMatch;

        isApplying = true;

        // 실제 디바이스 비율을 기준으로 CanvasScaler Match를 먼저 변경합니다.
        if (canvasScaler != null)
        {
            canvasScaler.matchWidthOrHeight = targetMatch;
        }

        // CanvasScaler 변경 내용을 현재 프레임의 Canvas Rect에 즉시 반영합니다.
        Canvas.ForceUpdateCanvases();

        Vector2 parentSize = parentRect.rect.size;

        if (parentSize.x > 0f && parentSize.y > 0f)
        {
            scalerRect.pivot = new Vector2(0.5f, 0.5f);
            scalerRect.anchoredPosition = Vector2.zero;
            scalerRect.localScale = Vector3.one;

            if (isNarrow)
            {
                // 4:3처럼 가로가 부족한 경우:
                // 부모 가로 전체를 사용하고 세로를 16:9 높이로 제한합니다.
                scalerRect.anchorMin = new Vector2(0.5f, 0.5f);
                scalerRect.anchorMax = new Vector2(0.5f, 0.5f);

                scalerRect.sizeDelta = new Vector2(
                    parentSize.x,
                    parentSize.x / TargetAspect
                );
            }
            else
            {
                // 16:9 또는 더 넓은 경우 부모 전체를 채웁니다.
                scalerRect.anchorMin = Vector2.zero;
                scalerRect.anchorMax = Vector2.one;
                scalerRect.offsetMin = Vector2.zero;
                scalerRect.offsetMax = Vector2.zero;
            }
        }

        if (logAspectChanges &&
            (!hasApplied || isNarrow != lastWasNarrow))
        {
            Debug.Log(
                "[LandscapeAspectRatioScaler] " +
                screenWidth + "x" + screenHeight +
                " / Aspect " + screenAspect.ToString("0.000") +
                " / Narrow " + isNarrow +
                " / CanvasScaler Match " + targetMatch,
                this
            );
        }

        lastScreenWidth = screenWidth;
        lastScreenHeight = screenHeight;
        lastParentSize = parentRect.rect.size;
        lastWasNarrow = isNarrow;
        hasApplied = true;

        isApplying = false;
    }

    private void ResolveReferences()
    {
        if (scalerRect == null)
        {
            scalerRect = GetComponent<RectTransform>();
        }

        if (scalerRect != null)
        {
            RectTransform currentParent =
                scalerRect.parent as RectTransform;

            if (parentRect != currentParent)
            {
                parentRect = currentParent;
            }
        }

        if (canvasScaler == null)
        {
            canvasScaler =
                GetComponentInParent<CanvasScaler>(true);
        }
    }
}