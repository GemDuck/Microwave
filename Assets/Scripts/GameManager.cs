using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public enum Intensity { Weak, Medium, Strong } // 강도: 약, 중, 강

    [Header("1. 기본 UI 연결")]
    public RectTransform plateTransform;   // 회전 원판 (그릇)
    public Image foodImage;                // 음식 이미지
    public Slider gaugeSlider;             // 하단 게이지 슬라이더

    [Header("2. 리듬 판정 원 설정")]
    public GameObject rhythmTargetPrefab;  // 방금 만든 RhythmTarget 프리팹
    public RectTransform spawnArea;        // 원이 생성될 부모 영역 (Canvas 또는 원판 주변 패널)
    public RectTransform[] spawnPoints;    // 원이 나타날 지정 위치들 (비워두면 원판 주변 랜덤 생성)
    public TextMeshProUGUI judgeText;      // 화면에 PERFECT/GREAT 등을 띄울 텍스트

    [Header("3. 난이도(강도) 및 속도 설정")]
    public Intensity currentIntensity = Intensity.Medium;
    [Range(0.1f, 2f)] public float rotationMultiplier = 0.5f; // 회전 감도 (0.5 = 기존 절반 속도로 묵직하게 회전)
    public float targetSpeed = 180f;       // 적정 회전 속도 (360 -> 180으로 하향 조정)
    public float maxOverloadSpeed = 360f;  // 과부하 속도 (720 -> 360으로 하향 조정)
    public float currentSpeed;

    [Header("4. 게이지 & 유예(관성) 설정")]
    [Range(0f, 1f)] public float gaugeValue = 0.5f; // 기본 시작 값 0.5 (정중앙)
    public float gaugeMoveSpeed = 2.0f;
    public float graceDuration = 1.8f;     // 중간에서 좌측 끝까지 버텨주는 시간 (1.5~2초)
    public float graceDropSpeed = 0.06f;
    public float accelDropMultiplier = 0.5f;

    [Header("5. 음식 스프라이트 & 만족도")]
    public Sprite rawFoodSprite;           // 덜 익음 (좌측 빨강)
    public Sprite cookedFoodSprite;        // 적당함 (중앙 초록)
    public Sprite burntFoodSprite;         // 탐 (우측 빨강)
    public float totalAccuracyScore = 0f;  // 누적 정확도 및 만족도

    private bool isDragging = false;
    private Vector2 prevMouseDir;
    private Camera uiCamera;

    // 유예(관성) 타이머
    private float sweetSpotStayTimer = 0f;
    private bool isGraceUnlocked = false;
    private float graceDropTimer = 0f;

    // 리듬 원 생성 타이머
    private float spawnTimer = 0f;
    private List<float> accuracyHistory = new List<float>();

    void Start()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = canvas.worldCamera;

        // 시작 게이지 0.5 고정 및 초기 유예 활성화
        gaugeValue = 0.5f;
        isGraceUnlocked = true;
        graceDropTimer = 0f;

        if (gaugeSlider != null)
        {
            gaugeSlider.minValue = 0f;
            gaugeSlider.maxValue = 1f;
            gaugeSlider.value = gaugeValue; // 실행 즉시 슬라이더 핸들을 중앙(0.5)에 배치
            gaugeSlider.interactable = false;
        }
    }

    void Update()
    {
        HandlePlateRotation();
        UpdateGaugeLogic();
        HandleRhythmSpawning();
        UpdateFoodState();
    }

    // 1. 그릇(원판) 시계 방향 회전 입력
    void HandlePlateRotation()
    {
        // 마우스 장치가 연결되어 있지 않으면 실행 안 함
        if (Mouse.current == null) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector2 plateCenter = RectTransformUtility.WorldToScreenPoint(uiCamera, plateTransform.position);
            Vector2 offsetFromCenter = mousePos - plateCenter;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(plateTransform, mousePos, uiCamera))
                {
                    isDragging = true;
                    prevMouseDir = offsetFromCenter.normalized;
                }
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;
            }

            float targetInstantSpeed = 0f;

            // 중심에서 너무 가까운 곳(30픽셀 이내)을 돌릴 때 각도가 확 튀는 현상 방지
            if (isDragging && offsetFromCenter.magnitude > 30f)
            {
                Vector2 currentMouseDir = offsetFromCenter.normalized;
                float deltaAngle = -Vector2.SignedAngle(prevMouseDir, currentMouseDir);

                // 시계 방향 회전만 인정하되, 한 프레임당 최대 15도 이상 급발진하지 않도록 제한
                if (deltaAngle > 0f)
                {
                    float clampedDelta = Mathf.Min(deltaAngle, 15f);
                    float adjustedAngle = clampedDelta * rotationMultiplier;

                    // 1. 실제 원판 이미지 회전
                    plateTransform.Rotate(0f, 0f, -adjustedAngle);

                    // 2. 프레임 드랍/고주사율에 휘둘리지 않도록 고정 기준(60fps 환산)으로 속도 계산
                    targetInstantSpeed = adjustedAngle * 60f;
                }

                prevMouseDir = currentMouseDir;
            }
            else if (isDragging)
            {
                prevMouseDir = offsetFromCenter.normalized;
            }

            // currentSpeed가 확 튀지 않고 묵직하게 따라오도록 보간 속도를 15f -> 4f로 낮춤
            float smoothSpeed = (targetInstantSpeed > 0f) ? 4f : 3f;
            currentSpeed = Mathf.Lerp(currentSpeed, targetInstantSpeed, Time.deltaTime * smoothSpeed);
        }

        // 2. 게이지 변화 및 리듬 클릭을 위한 1.5~2초 유예(관성) 로직
        void UpdateGaugeLogic()
    {
        bool isSweetSpot = (gaugeValue >= 0.4f && gaugeValue <= 0.6f);

        if (isSweetSpot)
        {
            sweetSpotStayTimer += Time.deltaTime;
            if (sweetSpotStayTimer >= 2.0f)
            {
                isGraceUnlocked = true;
                graceDropTimer = 0f;
            }
        }
        else
        {
            sweetSpotStayTimer = 0f;
        }

        bool isInputLack = (!isDragging || currentSpeed < targetSpeed * 0.3f);

        if (isInputLack)
        {
            if (isGraceUnlocked)
            {
                graceDropTimer += Time.deltaTime;

                if (graceDropTimer <= graceDuration)
                {
                    // 유예 시간 동안은 천천히 하락 (플레이어가 리듬 원을 클릭할 시간 확보)
                    gaugeValue -= graceDropSpeed * Time.deltaTime;
                }
                else
                {
                    // 유예 시간 초과 시 가속 하락
                    float overTime = graceDropTimer - graceDuration;
                    gaugeValue -= (graceDropSpeed + overTime * accelDropMultiplier) * Time.deltaTime;
                }

                if (gaugeValue <= 0.01f)
                {
                    isGraceUnlocked = false;
                    graceDropTimer = 0f;
                }
            }
            else
            {
                gaugeValue -= 0.35f * Time.deltaTime;
            }
        }
        else
        {
            graceDropTimer = 0f;
            float targetGauge = 0.5f;

            if (currentSpeed <= targetSpeed)
                targetGauge = Mathf.Lerp(0.0f, 0.5f, currentSpeed / targetSpeed);
            else
                targetGauge = Mathf.Lerp(0.5f, 1.0f, Mathf.Clamp01((currentSpeed - targetSpeed) / (maxOverloadSpeed - targetSpeed)));

            float moveRate = (targetGauge > 0.6f) ? gaugeMoveSpeed * 1.5f : gaugeMoveSpeed;
            gaugeValue = Mathf.MoveTowards(gaugeValue, targetGauge, moveRate * Time.deltaTime);
        }

        gaugeValue = Mathf.Clamp01(gaugeValue);
        if (gaugeSlider != null) gaugeSlider.value = gaugeValue;
    }

    // 3. 강도(약, 중, 강)에 따른 리듬 원 생성
    void HandleRhythmSpawning()
    {
        if (rhythmTargetPrefab == null || spawnArea == null) return;

        // 강도가 강해질수록 패턴이 자주 나옴
        float spawnInterval = currentIntensity switch
        {
            Intensity.Weak => 3.5f,
            Intensity.Medium => 2.2f,
            Intensity.Strong => 1.3f,
            _ => 2.2f
        };

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            SpawnRhythmCircle();
        }
    }

    void SpawnRhythmCircle()
    {
        GameObject obj = Instantiate(rhythmTargetPrefab, spawnArea);
        RectTransform rect = obj.GetComponent<RectTransform>();

        // 지정된 스폰 포인트가 있으면 그중 랜덤 선택, 없으면 원판 반경 주변 랜덤 위치
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            RectTransform randomPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
            rect.position = randomPoint.position;
        }
        else
        {
            float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float radius = plateTransform.rect.width * 0.45f;
            Vector2 offset = new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle)) * radius;
            rect.anchoredPosition = plateTransform.anchoredPosition + offset;
        }

        RhythmTarget target = obj.GetComponent<RhythmTarget>();
        // 1.2초 동안 링이 줄어들도록 설정 (속도 조절 가능)
        target.Init(1.2f, OnRhythmJudged);
    }

    // 4. 리듬 원 클릭 결과 수신 및 만족도 반영
    void OnRhythmJudged(string rank, float accuracy, Color color)
    {
        accuracyHistory.Add(accuracy);

        // 평균 정확도 계산 (0 ~ 100%)
        float sum = 0f;
        foreach (float acc in accuracyHistory) sum += acc;
        totalAccuracyScore = (sum / accuracyHistory.Count) * 100f;

        if (judgeText != null)
        {
            judgeText.text = rank;
            judgeText.color = color;
        }
    }

    // 5. 게이지 위치에 따른 음식 상태 변경
    void UpdateFoodState()
    {
        if (gaugeValue < 0.4f)
        {
            if (foodImage != null && rawFoodSprite != null) foodImage.sprite = rawFoodSprite;
        }
        else if (gaugeValue <= 0.6f)
        {
            if (foodImage != null && cookedFoodSprite != null) foodImage.sprite = cookedFoodSprite;
        }
        else
        {
            if (foodImage != null && burntFoodSprite != null) foodImage.sprite = burntFoodSprite;
        }
    }
}