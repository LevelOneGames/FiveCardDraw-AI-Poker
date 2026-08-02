using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 베팅 칩을 미리 생성해 두고 재사용하는 간단한 오브젝트 풀입니다.
/// </summary>
[DisallowMultipleComponent]
public class PokerChipPool : MonoBehaviour
{
    [Header("Pool")]
    [Tooltip("RectTransform + Image + PokerChipVisual로 만든 UI 칩 프리팹입니다.")]
    public PokerChipVisual chipPrefab;

    [Tooltip("사용하지 않는 칩을 보관할 부모입니다. 비워두면 이 오브젝트를 사용합니다.")]
    public RectTransform poolContainer;

    [Min(0)]
    public int prewarmCount = 60;

    [Tooltip("풀이 부족할 때 자동으로 칩을 추가 생성합니다.")]
    public bool allowPoolGrowth = true;

    private readonly Queue<PokerChipVisual> available =
        new Queue<PokerChipVisual>();

    private readonly HashSet<PokerChipVisual> allInstances =
        new HashSet<PokerChipVisual>();

    private bool isInitialized;

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;

        if (poolContainer == null)
        {
            poolContainer = transform as RectTransform;
        }

        if (chipPrefab == null)
        {
            Debug.LogError("PokerChipPool에 Chip Prefab이 연결되지 않았습니다.");
            return;
        }

        for (int i = 0; i < prewarmCount; i++)
        {
            PokerChipVisual chip = CreateInstance();

            if (chip != null)
            {
                available.Enqueue(chip);
            }
        }
    }

    public PokerChipVisual Get(RectTransform activeParent)
    {
        Initialize();

        PokerChipVisual chip = null;

        while (available.Count > 0 && chip == null)
        {
            chip = available.Dequeue();
        }

        if (chip == null && allowPoolGrowth)
        {
            chip = CreateInstance();
        }

        if (chip == null)
        {
            return null;
        }

        RectTransform rect = chip.RectTransform;

        if (rect != null && activeParent != null)
        {
            rect.SetParent(activeParent, false);
            rect.SetAsLastSibling();
        }

        chip.gameObject.SetActive(true);
        return chip;
    }

    public void Release(PokerChipVisual chip)
    {
        if (chip == null || !allInstances.Contains(chip))
        {
            return;
        }

        chip.InvalidateAndHide();

        RectTransform rect = chip.RectTransform;

        if (rect != null && poolContainer != null)
        {
            rect.SetParent(poolContainer, false);
        }

        available.Enqueue(chip);
    }

    private PokerChipVisual CreateInstance()
    {
        if (chipPrefab == null)
        {
            return null;
        }

        Transform parent =
            poolContainer != null
                ? poolContainer
                : transform;

        PokerChipVisual chip = Instantiate(
            chipPrefab,
            parent
        );

        chip.name = chipPrefab.name + "_Pooled";
        chip.gameObject.SetActive(false);

        allInstances.Add(chip);
        return chip;
    }
}