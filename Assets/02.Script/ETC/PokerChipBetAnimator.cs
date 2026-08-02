using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 금액을 칩 단위로 분해하고 각 플레이어 위치에서 중앙 테이블로 던지는 UI 연출을 담당합니다.
/// 칩은 PokerChipPool을 통해 재사용됩니다.
/// </summary>
[DisallowMultipleComponent]
public class PokerChipBetAnimator : MonoBehaviour
{
    [Serializable]
    public class ChipDenomination
    {
        [Tooltip("이 칩이 나타내는 금액입니다.")]
        public long value = 10_000L;

        [FormerlySerializedAs("sprite")]
        [Tooltip("이 금액에 사용할 칩 스프라이트를 직접 연결합니다.")]
        public Sprite chipSprite;

        [Tooltip("완성된 컬러 칩 이미지는 흰색으로 둡니다. 흰색 원본에 색을 입힐 때만 변경합니다.")]
        public Color tint = Color.white;

        [Tooltip("칩 안의 별도 Text에 표시할 선택형 문구입니다. 스프라이트 자체에 숫자가 있으면 비워도 됩니다.")]
        public string label = string.Empty;
    }

    private sealed class ChipThrowRequest
    {
        public ChipDenomination denomination;
        public long representedAmount;
    }

    private sealed class ChipCollectState
    {
        public PokerChipVisual chip;
        public Vector3 startPosition;
        public Vector3 endPosition;
        public Vector3 controlPosition;
        public Vector3 startScale;
        public float startAngle;
        public float endAngle;
        public float delay;
        public float duration;
        public bool completed;
    }

    [Header("Required References")]
    public PokerChipPool chipPool;

    [Tooltip("날아가는 칩과 테이블 위 칩이 배치될 전체 화면용 RectTransform입니다.")]
    public RectTransform chipLayer;

    [Tooltip("칩이 최종적으로 놓일 중앙 영역입니다. RectTransform 크기가 곧 최종 위치 범위입니다.")]
    public RectTransform landingArea;

    [Tooltip("Player Number 0~4 순서로 칩 출발 위치를 등록합니다. 각 플레이어 카드 덱 뒤쪽에 빈 RectTransform을 두는 것을 권장합니다.")]
    public RectTransform[] playerThrowOrigins = new RectTransform[5];

    [Header("Chip Denominations")]
    [Tooltip("큰 금액부터 등록하지 않아도 실행 시 자동 정렬됩니다.")]
    public List<ChipDenomination> denominations =
        new List<ChipDenomination>();

    [Tooltip("금액이 가장 작은 칩 단위로 나누어 떨어지지 않을 때 가장 작은 칩 한 개로 잔액을 표현합니다. 실제 게임 금액에는 영향이 없습니다.")]
    public bool useSmallestChipForRemainder = true;

    [Header("Visual Chip Count")]
    [Min(1)]
    [Tooltip("한 번의 베팅에서 화면에 던지는 최대 칩 수입니다.")]
    public int maxVisualChipsPerBet = 12;

    [Min(1)]
    [Tooltip("테이블 위에 유지할 최대 칩 수입니다. 초과하면 가장 오래된 칩부터 풀로 돌아갑니다.")]
    public int maxChipsOnTable = 90;

    [Header("Chip Size")]
    public Vector2 chipSize = new Vector2(42f, 42f);
    public Vector2 randomScaleRange = new Vector2(0.88f, 1.08f);

    [Header("Throw Timing")]
    [Min(0f)]
    public float intervalBetweenChips = 0.045f;

    [Tooltip("칩 한 개가 날아가는 최소/최대 시간입니다.")]
    public Vector2 throwDurationRange = new Vector2(0.38f, 0.62f);

    [Tooltip("0에서 1까지 이동 속도 곡선입니다.")]
    public AnimationCurve travelCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Throw Motion")]
    [Tooltip("포물선 높이의 최소/최대값입니다. Chip Layer의 로컬 UI 좌표 기준입니다.")]
    public Vector2 arcHeightRange = new Vector2(90f, 170f);

    [Tooltip("좌우로 살짝 휘는 정도입니다.")]
    public Vector2 lateralCurveRange = new Vector2(-45f, 45f);

    [Tooltip("비행 중 회전하는 바퀴 수의 최소/최대값입니다.")]
    public Vector2 rotationTurnsRange = new Vector2(0.8f, 2.4f);

    [Tooltip("출발 위치가 완전히 겹치지 않도록 주는 랜덤 오프셋입니다.")]
    public Vector2 startJitter = new Vector2(16f, 10f);

    [Tooltip("착지 직전 살짝 커졌다가 원래 크기로 돌아오는 정도입니다.")]
    [Range(0f, 0.35f)]
    public float flightScalePulse = 0.12f;

    [Header("Landing Area")]
    [Tooltip("직사각형 대신 타원 형태로 중앙에 모이게 합니다.")]
    public bool useEllipseLandingArea = true;

    [Range(0.05f, 1f)]
    [Tooltip("Landing Area 안에서 실제 사용하는 가로 비율입니다.")]
    public float landingHorizontalUsage = 0.92f;

    [Range(0.05f, 1f)]
    [Tooltip("Landing Area 안에서 실제 사용하는 세로 비율입니다.")]
    public float landingVerticalUsage = 0.82f;

    [Header("Depth Appearance")]
    [Min(0)]
    [Tooltip("테이블 칩 수가 이 값 이하일 때는 모든 칩을 밝게 유지합니다.")]
    public int darkeningStartCount = 40;

    [Min(1)]
    [Tooltip("테이블 칩 수가 이 값에 도달할 때 가장 오래된 칩이 Oldest Chip Brightness까지 어두워집니다.")]
    public int darkeningFullCount = 120;

    [Range(0.1f, 1f)]
    [Tooltip("칩이 충분히 많이 쌓였을 때 가장 먼저 놓인 칩의 최소 밝기입니다.")]
    public float oldestChipBrightness = 0.72f;

    [Range(0.1f, 1f)]
    [Tooltip("가장 최근에 놓인 앞쪽 칩의 밝기입니다.")]
    public float newestChipBrightness = 1f;

    [Range(0.1f, 5f)]
    [Tooltip("값이 클수록 뒤쪽 칩만 천천히 어두워지고, 최근 칩들은 더 오래 밝게 유지됩니다.")]
    public float depthDarkeningExponent = 1.8f;

    [Header("Winner Chip Collection")]
    [Tooltip("위너 표시 후 테이블 칩이 승자에게 이동할 때 칩 사이의 출발 간격입니다.")]
    [Min(0f)]
    public float collectIntervalBetweenChips = 0.025f;

    [Tooltip("칩 한 개가 승자 위치까지 이동하는 최소/최대 시간입니다.")]
    public Vector2 collectDurationRange = new Vector2(0.42f, 0.68f);

    [Tooltip("승자에게 모일 때 포물선 높이의 최소/최대값입니다.")]
    public Vector2 collectArcHeightRange = new Vector2(45f, 110f);

    [Tooltip("승자에게 이동할 때 좌우로 휘는 정도입니다.")]
    public Vector2 collectLateralCurveRange = new Vector2(-35f, 35f);

    [Tooltip("승자 위치에 도착할 때 칩이 작아지는 최종 배율입니다.")]
    [Range(0.01f, 1f)]
    public float collectEndScale = 0.25f;

    [Tooltip("승자 위치에 칩이 완전히 같은 지점으로 모이지 않도록 주는 오프셋입니다.")]
    public Vector2 collectTargetJitter = new Vector2(14f, 9f);

    [Tooltip("0에서 1까지 승자 방향 이동 속도 곡선입니다.")]
    public AnimationCurve collectTravelCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Time")]
    [Tooltip("일시정지 상태에서도 연출해야 한다면 켭니다.")]
    public bool useUnscaledTime = false;

    private readonly List<PokerChipVisual> activeChips =
        new List<PokerChipVisual>();

    private readonly List<Coroutine> runningThrowRoutines =
        new List<Coroutine>();

    private int sortingSequence;

    public bool HasActiveTableChips
    {
        get
        {
            RemoveInvalidActiveChips();
            return activeChips.Count > 0;
        }
    }

    private void Awake()
    {
        EnsureDefaultDenominations();
    }

    private void OnDisable()
    {
        ClearTableChips();
    }

    /// <summary>
    /// 지정 플레이어가 실제로 지불한 금액만큼 칩 투척 연출을 시작합니다.
    /// </summary>
    public void PlayBet(PlayerControl player, long paidAmount)
    {
        if (player == null)
        {
            return;
        }

        PlayBet(player.playerNumber, paidAmount);
    }

    public void PlayBet(int playerNumber, long paidAmount)
    {
        if (paidAmount <= 0L)
        {
            return;
        }

        if (chipPool == null || chipLayer == null || landingArea == null)
        {
            Debug.LogWarning(
                "PokerChipBetAnimator의 Pool, Chip Layer, Landing Area 연결을 확인하세요."
            );
            return;
        }

        RectTransform origin = GetOrigin(playerNumber);

        if (origin == null)
        {
            Debug.LogWarning(
                "Player " + playerNumber +
                "의 Chip Throw Origin이 연결되지 않았습니다."
            );
            return;
        }

        List<ChipThrowRequest> requests =
            BuildChipRequests(paidAmount);

        if (requests.Count == 0)
        {
            return;
        }

        Coroutine routine = StartCoroutine(
            ThrowBetRoutine(origin, requests)
        );

        runningThrowRoutines.Add(routine);
    }

    /// <summary>
    /// 위너 표시 후 테이블 위의 모든 칩을 승자 위치로 이동시킨 뒤 풀로 반환합니다.
    /// 승자가 여러 명이면 화면의 칩을 승자들에게 순서대로 나누어 보냅니다.
    /// </summary>
    public IEnumerator CollectTableChipsToWinnersRoutine(
        IList<PlayerControl> winners)
    {
        StopRunningThrowRoutines();
        RemoveInvalidActiveChips();

        if (activeChips.Count == 0)
        {
            yield break;
        }

        List<RectTransform> winnerTargets =
            new List<RectTransform>();

        if (winners != null)
        {
            for (int i = 0; i < winners.Count; i++)
            {
                PlayerControl winner = winners[i];

                if (winner == null)
                {
                    continue;
                }

                RectTransform target = GetOrigin(winner.playerNumber);

                if (target != null && !winnerTargets.Contains(target))
                {
                    winnerTargets.Add(target);
                }
            }
        }

        if (winnerTargets.Count == 0 ||
            chipLayer == null ||
            chipPool == null)
        {
            ClearTableChips();
            yield break;
        }

        List<PokerChipVisual> chipsToCollect =
            new List<PokerChipVisual>(activeChips);

        List<ChipCollectState> states =
            new List<ChipCollectState>(chipsToCollect.Count);

        float maximumEndTime = 0f;

        // 가장 최근에 쌓인 앞쪽 칩부터 먼저 승자에게 날아가게 합니다.
        for (int order = 0; order < chipsToCollect.Count; order++)
        {
            int chipIndex = chipsToCollect.Count - 1 - order;
            PokerChipVisual chip = chipsToCollect[chipIndex];

            if (chip == null || !chip.gameObject.activeSelf)
            {
                continue;
            }

            RectTransform rect = chip.RectTransform;
            RectTransform target =
                winnerTargets[order % winnerTargets.Count];

            Vector3 start = rect.localPosition;
            Vector3 end = chipLayer.InverseTransformPoint(target.position);

            end += new Vector3(
                UnityEngine.Random.Range(
                    -collectTargetJitter.x,
                    collectTargetJitter.x
                ),
                UnityEngine.Random.Range(
                    -collectTargetJitter.y,
                    collectTargetJitter.y
                ),
                0f
            );

            float arcHeight = UnityEngine.Random.Range(
                Mathf.Min(collectArcHeightRange.x, collectArcHeightRange.y),
                Mathf.Max(collectArcHeightRange.x, collectArcHeightRange.y)
            );

            float lateral = UnityEngine.Random.Range(
                Mathf.Min(collectLateralCurveRange.x, collectLateralCurveRange.y),
                Mathf.Max(collectLateralCurveRange.x, collectLateralCurveRange.y)
            );

            float duration = UnityEngine.Random.Range(
                Mathf.Min(collectDurationRange.x, collectDurationRange.y),
                Mathf.Max(collectDurationRange.x, collectDurationRange.y)
            );

            duration = Mathf.Max(0.05f, duration);

            float delay = order * Mathf.Max(0f, collectIntervalBetweenChips);
            float startAngle = rect.localEulerAngles.z;
            float rotationDirection =
                UnityEngine.Random.value < 0.5f ? -1f : 1f;

            ChipCollectState state = new ChipCollectState
            {
                chip = chip,
                startPosition = start,
                endPosition = end,
                controlPosition =
                    (start + end) * 0.5f +
                    Vector3.up * arcHeight +
                    Vector3.right * lateral,
                startScale = rect.localScale,
                startAngle = startAngle,
                endAngle = startAngle +
                           360f *
                           UnityEngine.Random.Range(0.6f, 1.5f) *
                           rotationDirection,
                delay = delay,
                duration = duration,
                completed = false
            };

            chip.SetDepthBrightness(1f);
            rect.SetAsLastSibling();

            states.Add(state);
            maximumEndTime = Mathf.Max(
                maximumEndTime,
                delay + duration
            );
        }

        float elapsed = 0f;

        while (elapsed < maximumEndTime)
        {
            float delta = useUnscaledTime
                ? Time.unscaledDeltaTime
                : Time.deltaTime;

            elapsed += delta;

            for (int i = 0; i < states.Count; i++)
            {
                ChipCollectState state = states[i];

                if (state.completed ||
                    state.chip == null ||
                    !state.chip.gameObject.activeSelf)
                {
                    continue;
                }

                float localElapsed = elapsed - state.delay;

                if (localElapsed < 0f)
                {
                    continue;
                }

                float normalized = Mathf.Clamp01(
                    localElapsed / state.duration
                );

                float t = collectTravelCurve != null
                    ? Mathf.Clamp01(
                        collectTravelCurve.Evaluate(normalized)
                    )
                    : normalized;

                float oneMinusT = 1f - t;

                Vector3 position =
                    oneMinusT * oneMinusT * state.startPosition +
                    2f * oneMinusT * t * state.controlPosition +
                    t * t * state.endPosition;

                RectTransform rect = state.chip.RectTransform;
                rect.localPosition = position;
                rect.localEulerAngles = new Vector3(
                    0f,
                    0f,
                    Mathf.LerpUnclamped(
                        state.startAngle,
                        state.endAngle,
                        t
                    )
                );

                rect.localScale = Vector3.LerpUnclamped(
                    state.startScale,
                    Vector3.one * Mathf.Max(0.01f, collectEndScale),
                    t
                );

                if (normalized >= 1f)
                {
                    state.completed = true;
                    activeChips.Remove(state.chip);
                    chipPool.Release(state.chip);
                }
            }

            yield return null;
        }

        // 프레임 누락이나 비활성화 등으로 남은 칩이 있다면 안전하게 모두 반환합니다.
        for (int i = states.Count - 1; i >= 0; i--)
        {
            PokerChipVisual chip = states[i].chip;

            if (chip != null && chip.gameObject.activeSelf)
            {
                activeChips.Remove(chip);
                chipPool.Release(chip);
            }
        }

        RemoveInvalidActiveChips();
        sortingSequence = activeChips.Count;
    }

    /// <summary>
    /// 새 게임 시작 시 테이블 위 칩과 진행 중 투척을 모두 풀로 되돌립니다.
    /// </summary>
    public void ClearTableChips()
    {
        StopRunningThrowRoutines();

        if (chipPool != null)
        {
            for (int i = activeChips.Count - 1; i >= 0; i--)
            {
                if (activeChips[i] != null)
                {
                    chipPool.Release(activeChips[i]);
                }
            }
        }

        activeChips.Clear();
        sortingSequence = 0;
    }

    private void StopRunningThrowRoutines()
    {
        for (int i = 0; i < runningThrowRoutines.Count; i++)
        {
            if (runningThrowRoutines[i] != null)
            {
                StopCoroutine(runningThrowRoutines[i]);
            }
        }

        runningThrowRoutines.Clear();
    }

    private void RemoveInvalidActiveChips()
    {
        for (int i = activeChips.Count - 1; i >= 0; i--)
        {
            if (activeChips[i] == null ||
                !activeChips[i].gameObject.activeSelf)
            {
                activeChips.RemoveAt(i);
            }
        }
    }

    private IEnumerator ThrowBetRoutine(
        RectTransform origin,
        List<ChipThrowRequest> requests)
    {
        for (int i = 0; i < requests.Count; i++)
        {
            SpawnAndThrowChip(origin, requests[i]);

            if (i < requests.Count - 1 && intervalBetweenChips > 0f)
            {
                if (useUnscaledTime)
                {
                    yield return new WaitForSecondsRealtime(
                        intervalBetweenChips
                    );
                }
                else
                {
                    yield return new WaitForSeconds(
                        intervalBetweenChips
                    );
                }
            }
        }
    }

    private void SpawnAndThrowChip(
        RectTransform origin,
        ChipThrowRequest request)
    {
        PokerChipVisual chip = chipPool.Get(chipLayer);

        if (chip == null)
        {
            return;
        }

        float randomScale = UnityEngine.Random.Range(
            Mathf.Min(randomScaleRange.x, randomScaleRange.y),
            Mathf.Max(randomScaleRange.x, randomScaleRange.y)
        );

        int version = chip.Prepare(
            request.denomination.chipSprite,
            request.denomination.tint,
            request.denomination.label,
            chipSize,
            randomScale
        );

        sortingSequence++;
        chip.RectTransform.SetAsLastSibling();

        activeChips.Add(chip);
        TrimTableChipOverflow();
        RefreshDepthBrightness();

        Vector3 startLocal =
            chipLayer.InverseTransformPoint(origin.position);

        startLocal += new Vector3(
            UnityEngine.Random.Range(-startJitter.x, startJitter.x),
            UnityEngine.Random.Range(-startJitter.y, startJitter.y),
            0f
        );

        Vector3 endWorld = GetRandomLandingWorldPosition();
        Vector3 endLocal = chipLayer.InverseTransformPoint(endWorld);

        Coroutine routine = StartCoroutine(
            AnimateChipRoutine(
                chip,
                version,
                startLocal,
                endLocal,
                randomScale
            )
        );

        runningThrowRoutines.Add(routine);
    }

    private IEnumerator AnimateChipRoutine(
        PokerChipVisual chip,
        int version,
        Vector3 start,
        Vector3 end,
        float baseScale)
    {
        float duration = UnityEngine.Random.Range(
            Mathf.Min(throwDurationRange.x, throwDurationRange.y),
            Mathf.Max(throwDurationRange.x, throwDurationRange.y)
        );

        duration = Mathf.Max(0.05f, duration);

        float arcHeight = UnityEngine.Random.Range(
            Mathf.Min(arcHeightRange.x, arcHeightRange.y),
            Mathf.Max(arcHeightRange.x, arcHeightRange.y)
        );

        float lateral = UnityEngine.Random.Range(
            Mathf.Min(lateralCurveRange.x, lateralCurveRange.y),
            Mathf.Max(lateralCurveRange.x, lateralCurveRange.y)
        );

        float turnCount = UnityEngine.Random.Range(
            Mathf.Min(rotationTurnsRange.x, rotationTurnsRange.y),
            Mathf.Max(rotationTurnsRange.x, rotationTurnsRange.y)
        );

        float rotationDirection =
            UnityEngine.Random.value < 0.5f ? -1f : 1f;

        float startAngle = UnityEngine.Random.Range(-180f, 180f);
        float endAngle =
            startAngle + 360f * turnCount * rotationDirection;

        Vector3 middle = (start + end) * 0.5f;
        Vector3 control = middle +
                          Vector3.up * arcHeight +
                          Vector3.right * lateral;

        RectTransform rect = chip.RectTransform;
        rect.localPosition = start;
        rect.localEulerAngles = new Vector3(0f, 0f, startAngle);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (!chip.IsSpawnVersion(version))
            {
                yield break;
            }

            float delta =
                useUnscaledTime
                    ? Time.unscaledDeltaTime
                    : Time.deltaTime;

            elapsed += delta;

            float normalized = Mathf.Clamp01(elapsed / duration);
            float t =
                travelCurve != null
                    ? Mathf.Clamp01(travelCurve.Evaluate(normalized))
                    : normalized;

            float oneMinusT = 1f - t;

            Vector3 position =
                oneMinusT * oneMinusT * start +
                2f * oneMinusT * t * control +
                t * t * end;

            rect.localPosition = position;
            rect.localEulerAngles = new Vector3(
                0f,
                0f,
                Mathf.LerpUnclamped(startAngle, endAngle, t)
            );

            float pulse = Mathf.Sin(normalized * Mathf.PI) * flightScalePulse;
            rect.localScale = Vector3.one * baseScale * (1f + pulse);

            yield return null;
        }

        if (!chip.IsSpawnVersion(version))
        {
            yield break;
        }

        rect.localPosition = end;
        rect.localEulerAngles = new Vector3(
            0f,
            0f,
            Mathf.Repeat(endAngle, 360f)
        );
        rect.localScale = Vector3.one * baseScale;

        RefreshDepthBrightness();
    }

    private RectTransform GetOrigin(int playerNumber)
    {
        if (playerThrowOrigins == null ||
            playerNumber < 0 ||
            playerNumber >= playerThrowOrigins.Length)
        {
            return null;
        }

        return playerThrowOrigins[playerNumber];
    }

    private Vector3 GetRandomLandingWorldPosition()
    {
        Vector3[] corners = new Vector3[4];
        landingArea.GetWorldCorners(corners);

        Vector3 center =
            (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;

        Vector3 horizontalHalf =
            (corners[3] - corners[0]) *
            0.5f * landingHorizontalUsage;

        Vector3 verticalHalf =
            (corners[1] - corners[0]) *
            0.5f * landingVerticalUsage;

        float x;
        float y;

        if (useEllipseLandingArea)
        {
            Vector2 point = UnityEngine.Random.insideUnitCircle;
            x = point.x;
            y = point.y;
        }
        else
        {
            x = UnityEngine.Random.Range(-1f, 1f);
            y = UnityEngine.Random.Range(-1f, 1f);
        }

        return center + horizontalHalf * x + verticalHalf * y;
    }

    private List<ChipThrowRequest> BuildChipRequests(long amount)
    {
        EnsureDefaultDenominations();

        List<ChipDenomination> usable =
            new List<ChipDenomination>();

        for (int i = 0; i < denominations.Count; i++)
        {
            ChipDenomination denomination = denominations[i];

            if (denomination != null && denomination.value > 0L)
            {
                usable.Add(denomination);
            }
        }

        usable.Sort(delegate (ChipDenomination a, ChipDenomination b)
        {
            return b.value.CompareTo(a.value);
        });

        List<ChipThrowRequest> result =
            new List<ChipThrowRequest>();

        if (usable.Count == 0)
        {
            return result;
        }

        long remaining = amount;
        int maximum = Mathf.Max(1, maxVisualChipsPerBet);

        for (int denominationIndex = 0;
             denominationIndex < usable.Count && remaining > 0L;
             denominationIndex++)
        {
            ChipDenomination denomination = usable[denominationIndex];
            long count = remaining / denomination.value;

            while (count > 0L && result.Count < maximum)
            {
                result.Add(new ChipThrowRequest
                {
                    denomination = denomination,
                    representedAmount = denomination.value
                });

                remaining -= denomination.value;
                count--;
            }

            if (result.Count >= maximum)
            {
                break;
            }
        }

        if (remaining > 0L)
        {
            ChipDenomination smallest = usable[usable.Count - 1];

            if (result.Count < maximum && useSmallestChipForRemainder)
            {
                result.Add(new ChipThrowRequest
                {
                    denomination = smallest,
                    representedAmount = remaining
                });
            }
            else if (result.Count > 0)
            {
                // 화면 칩 수 제한에 걸린 잔액은 마지막 칩에 시각적으로 합칩니다.
                result[result.Count - 1].representedAmount += remaining;
            }
        }

        // 같은 단위만 줄지어 날아오는 느낌을 줄이기 위해 순서를 살짝 섞습니다.
        for (int i = result.Count - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);
            ChipThrowRequest temp = result[i];
            result[i] = result[randomIndex];
            result[randomIndex] = temp;
        }

        return result;
    }

    private void TrimTableChipOverflow()
    {
        int maximum = Mathf.Max(1, maxChipsOnTable);

        while (activeChips.Count > maximum)
        {
            PokerChipVisual oldest = activeChips[0];
            activeChips.RemoveAt(0);

            if (oldest != null && chipPool != null)
            {
                chipPool.Release(oldest);
            }
        }
    }

    private void RefreshDepthBrightness()
    {
        RemoveInvalidActiveChips();

        if (activeChips.Count == 0)
        {
            return;
        }

        int startCount = Mathf.Max(0, darkeningStartCount);
        int fullCount = Mathf.Max(startCount + 1, darkeningFullCount);

        // 칩이 Start Count 이하일 때는 progress가 0이라 모든 칩이 밝게 유지됩니다.
        // Full Count에 가까워질수록 가장 오래된 칩의 목표 밝기가 천천히 낮아집니다.
        float stackProgress = Mathf.InverseLerp(
            startCount,
            fullCount,
            activeChips.Count
        );

        stackProgress = Mathf.SmoothStep(0f, 1f, stackProgress);

        float currentOldestBrightness = Mathf.Lerp(
            newestChipBrightness,
            oldestChipBrightness,
            stackProgress
        );

        float exponent = Mathf.Max(0.1f, depthDarkeningExponent);

        for (int i = 0; i < activeChips.Count; i++)
        {
            float ageNormalized =
                activeChips.Count <= 1
                    ? 1f
                    : (float)i / (activeChips.Count - 1);

            // 지수를 적용해 최근 칩은 더 오래 밝게 유지하고,
            // 오래된 뒤쪽 칩부터 자연스럽게 어두워지게 합니다.
            float brightnessPosition = Mathf.Pow(ageNormalized, exponent);

            float brightness = Mathf.Lerp(
                currentOldestBrightness,
                newestChipBrightness,
                brightnessPosition
            );

            activeChips[i].SetDepthBrightness(brightness);

            // 먼저 생성된 칩은 낮은 sibling, 나중 칩은 높은 sibling이 되어 앞에 표시됩니다.
            activeChips[i].RectTransform.SetSiblingIndex(i);
        }
    }

    private void EnsureDefaultDenominations()
    {
        if (denominations != null && denominations.Count > 0)
        {
            return;
        }

        denominations = new List<ChipDenomination>
        {
            CreateDefault(5_000_000L, new Color(0.12f, 0.12f, 0.15f), "5M"),
            CreateDefault(1_000_000L, new Color(0.42f, 0.20f, 0.72f), "1M"),
            CreateDefault(500_000L, new Color(0.16f, 0.56f, 0.30f), "500K"),
            CreateDefault(100_000L, new Color(0.92f, 0.43f, 0.08f), "100K"),
            CreateDefault(50_000L, new Color(0.76f, 0.14f, 0.18f), "50K"),
            CreateDefault(10_000L, new Color(0.05f, 0.52f, 0.88f), "10K"),
            CreateDefault(5_000L, new Color(0.90f, 0.72f, 0.12f), "5K"),
            CreateDefault(1_000L, new Color(0.82f, 0.84f, 0.88f), "1K")
        };
    }

    private ChipDenomination CreateDefault(
        long value,
        Color color,
        string label)
    {
        return new ChipDenomination
        {
            value = value,
            chipSprite = null,
            tint = color,
            label = label
        };
    }
}