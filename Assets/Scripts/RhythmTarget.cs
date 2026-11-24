using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;

public class RhythmTarget : MonoBehaviour, IPointerDownHandler
{
    [Header("UI 연결")]
    public RectTransform approachRing; // 바깥에서 줄어드는 얇은 파란 원
    public Image targetImage;          // 안쪽 하얀 원

    private float shrinkDuration = 2.0f; // 1.2초 -> 2.0초로 여유 확보
    private float timer = 0f;
    private bool isJudged = false;

    public Action<string, float, Color> onJudgedCallback;

    public void Init(float duration, Action<string, float, Color> callback)
    {
        shrinkDuration = duration;
        onJudgedCallback = callback;
        timer = 0f;
        isJudged = false;

        if (approachRing != null)
            approachRing.localScale = Vector3.one * 2.5f;
    }

    void Update()
    {
        if (isJudged) return;

        timer += Time.deltaTime;
        float progress = Mathf.Clamp01(timer / shrinkDuration);

        if (approachRing != null)
        {
            float currentScale = Mathf.Lerp(2.5f, 1.0f, progress);
            approachRing.localScale = Vector3.one * currentScale;
        }

        // 목표 시간보다 0.3초(300ms) 이상 늦으면 시간 초과 MISS
        if (timer > shrinkDuration + 0.3f)
        {
            Judge(999f, "시간 초과 MISS");
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isJudged) return;

        // 생성된 지 0.3초도 안 됐는데 눌린 경우(그릇 드래그와 겹침)는 무시
        if (timer < 0.3f) return;

        float diffMs = Mathf.Abs(timer - shrinkDuration) * 1000f;
        Judge(diffMs, $"오차: {diffMs:F0}ms");
    }

    void Judge(float diffMs, string debugReason)
    {
        isJudged = true;

        string rank;
        float accuracy;
        Color color;

        // 마우스 이동 시간을 고려한 현실적인 판정 범위
        if (diffMs <= 90f)
        {
            rank = "PERFECT";
            accuracy = 1.0f;
            color = new Color(1f, 0.84f, 0f); // 골드 옐로우
        }
        else if (diffMs <= 180f)
        {
            rank = "GREAT";
            accuracy = 0.55f;
            color = new Color(0.31f, 0.78f, 0.47f); // 에메랄드 그린
        }
        else if (diffMs <= 300f)
        {
            rank = "GOOD";
            accuracy = 0.20f;
            color = new Color(0.55f, 0.35f, 0.85f); // 퍼플 블루
        }
        else
        {
            rank = "MISS";
            accuracy = 0f;
            color = Color.red;
        }

        // 콘솔창에서 왜 MISS가 났는지(시간 초과인지, 몇 ms 차이였는지) 바로 확인 가능
        Debug.Log($"[리듬 판정] {rank} ({debugReason})");

        onJudgedCallback?.Invoke(rank, accuracy, color);
        Destroy(gameObject);
    }
}