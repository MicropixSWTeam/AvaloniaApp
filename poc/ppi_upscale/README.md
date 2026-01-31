# PPI Upscale

Pseudo-Panchromatic Image (PPI) 생성 및 Guided Upsampling 파이프라인.

## 개요

15채널 멀티스펙트럴 이미지(MSFA)에서 PPI를 생성하고, MSFA의 edge/smoothing 정보를 활용한 guided upsampling으로 2배 해상도로 업스케일. 이후 논문의 demosaicing 원리(Eq. 22)를 적용하여 모든 spectral 채널도 2배 업스케일.

```
Raw MSFA (15ch, 1012×1064)
         │
         ▼
    ┌─────────────┐
    │ PPI 생성    │  ← simple / ppid / igfppi
    └─────────────┘
         │
         ▼
    PPI (1012×1064)
         │
         ▼
    ┌─────────────┐
    │ Guided      │  ← MSFA에서 guide 계산
    │ Upsampling  │  ← guided / bicubic / lanczos
    └─────────────┘
         │
         ▼
    PPI 2x (2024×2128)
         │
         ▼
    ┌─────────────┐
    │ Spectral    │  ← BTES 방향성 보간
    │ Upsampling  │  ← Δ^c = channel - PPI
    └─────────────┘     Î^c = PPI_2x + Δ^c_2x
         │
         ▼
    Channels 2x (15ch, 2024×2128)
```

## 프로젝트 구조

```
ppi_upscale/
├── main.py                     # CLI 파이프라인
├── src/
│   ├── __init__.py
│   ├── base.py                 # PPIGeneratorBase (추상 클래스)
│   ├── ppi_simple.py           # PPISimple - 단순 평균
│   ├── ppi_ppid.py             # PPIPPID - Gaussian + high-freq correction
│   ├── ppi_igfppi.py           # PPIIGFPPI - Iterative Guided Filtering
│   ├── guided_upsample.py      # GuidedUpsampler - MSFA 기반 업스케일
│   ├── spectral_difference.py  # Spectral difference 계산 (Δ^c)
│   ├── btes_upsample.py        # BTES 방향성 보간 업스케일
│   ├── spectral_reconstruct.py # Spectral 채널 복원
│   └── spectral_upsampler.py   # SpectralUpsampler - 전체 wrapper
├── tests/
│   ├── test_ppi_generator.py
│   ├── test_guided_upsample.py
│   └── test_spectral_upsampler.py
├── data/                       # 입력 데이터 (410nm.png ~ 690nm.png)
└── output/                     # 출력 결과
```

## 설치

```bash
cd poc/ppi_upscale
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

## 사용법

### 기본 실행

```bash
python main.py
```

기본값: `igfppi` + `guided` 2x upscale

### 옵션

```bash
python main.py [OPTIONS]

옵션:
  -i, --input PATH        입력 디렉토리 (default: data)
  -o, --output-dir PATH   출력 디렉토리 (default: output)
  -m, --method            PPI 방법: simple, ppid, igfppi (default: igfppi)
  --upscale N             업스케일 배율 (default: 2)
  --upscale-method        업스케일 방법: guided, bicubic, lanczos (default: guided)
```

### 예시

```bash
# IGFPPI + Guided upscale
python main.py -m igfppi --upscale-method guided

# Simple PPI + Bicubic upscale
python main.py -m simple --upscale-method bicubic

# 모든 조합 실행
for ppi in simple ppid igfppi; do
  for up in guided bicubic lanczos; do
    python main.py -m $ppi --upscale-method $up -o output/${ppi}_${up}
  done
done
```

## PPI 생성 방법

| 방법 | 설명 |
|------|------|
| `simple` | 채널 평균 |
| `ppid` | Gaussian low-pass + high-frequency correction |
| `igfppi` | Iterative Guided Filtering (H/V 방향) |

## Upscale 방법

| 방법 | 설명 |
|------|------|
| `guided` | MSFA 15채널에서 edge+smoothing 정보 추출, guided filter 적용 |
| `bicubic` | Bicubic 보간 |
| `lanczos` | Lanczos 보간 (quintic spline) |

### Guided Upsampling 알고리즘

1. PPI를 bicubic으로 초기 업스케일
2. MSFA 15채널에서 guide 계산:
   - 각 채널의 Sobel edge magnitude 평균
   - 채널 평균 (structure info)
   - 두 정보 결합: `guide = mean_norm * (1 + edge_norm)`
3. Guide를 업스케일
4. Guided filter 적용하여 edge-aware refinement

## Spectral Channel Upsampling

논문 Section 3.2의 demosaicing 원리(Eq. 22)를 적용한 spectral 채널 업스케일:

```
Î^c = Î^PPI + Δ^c
```

### 알고리즘

1. **Spectral Difference 계산**: `Δ^c = channels[c] - PPI` (W×H)
2. **BTES Upsample**: `Δ^c_2x = btes_upsample(Δ^c, PPI_2x)` (2W×2H)
3. **Reconstruct**: `channels[c]_2x = PPI_2x + Δ^c_2x` (2W×2H)

### BTES 방향성 보간 (Eq. 18-21 응용)

2× 업스케일 후 픽셀 배치:
```
  j=0  1  2  3  4  5
i=0 [A] .  [B] .  [C] .     [X] = 원본 delta (짝수,짝수)
i=1  .  ?   .  ?   .  ?      ?  = Step1 대각선 보간 (홀수,홀수)
i=2 [D] .  [E] .  [F] .      .  = Step2 십자 보간
```

가중치 공식:
```
γ = 1 / (2|PPI_neighbor - PPI_center| + |PPI_neighbor - PPI_opposite| + ε)
```

- **Step 1**: 대각선 보간 (홀수,홀수) - 4개의 대각선 이웃 사용
- **Step 2a**: 수평 에지 보간 (짝수,홀수) - 좌우 원본 + 상하 Step1 결과
- **Step 2b**: 수직 에지 보간 (홀수,짝수) - 상하 원본 + 좌우 Step1 결과

## 출력 파일

```
output/
├── 1_ppi_{method}.png              # 원본 PPI (1012×1064)
├── 2_guide_msfa.png                # MSFA에서 계산된 guide
├── 3_ppi_{method}_2x_{upscale}.png # 업스케일된 PPI (2024×2128)
├── 4_channel_0_2x.png              # 업스케일된 채널 0 (410nm)
├── 4_channel_7_2x.png              # 업스케일된 채널 7 (550nm)
└── 4_channel_14_2x.png             # 업스케일된 채널 14 (690nm)
```

## 테스트

```bash
source .venv/bin/activate
pytest tests/ -v
```

총 66개 테스트:
- `test_ppi_generator.py`: PPI 생성 테스트
- `test_guided_upsample.py`: Guided upsampling 테스트
- `test_spectral_upsampler.py`: Spectral channel upsampling 테스트

## 의존성

- numpy
- scipy
- Pillow
- pytest (dev)
