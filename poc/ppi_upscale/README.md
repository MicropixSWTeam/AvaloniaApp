# PPI Upscale

Pseudo-Panchromatic Image (PPI) 생성 및 Guided Upsampling 파이프라인.

## 개요

15채널 멀티스펙트럴 이미지(MSFA)에서 PPI를 생성하고, MSFA의 edge/smoothing 정보를 활용한 guided upsampling으로 2배 해상도로 업스케일.

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
```

## 프로젝트 구조

```
ppi_upscale/
├── main.py                 # CLI 파이프라인
├── src/
│   ├── __init__.py
│   ├── base.py             # PPIGeneratorBase (추상 클래스)
│   ├── ppi_simple.py       # PPISimple - 단순 평균
│   ├── ppi_ppid.py         # PPIPPID - Gaussian + high-freq correction
│   ├── ppi_igfppi.py       # PPIIGFPPI - Iterative Guided Filtering
│   └── guided_upsample.py  # GuidedUpsampler - MSFA 기반 업스케일
├── tests/
│   ├── test_ppi_generator.py
│   └── test_guided_upsample.py
├── data/                   # 입력 데이터 (410nm.png ~ 690nm.png)
└── output/                 # 출력 결과
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

## 출력 파일

```
output/
├── 1_ppi_{method}.png              # 원본 PPI (1012×1064)
├── 2_guide_msfa.png                # MSFA에서 계산된 guide
└── 3_ppi_{method}_2x_{upscale}.png # 업스케일된 PPI (2024×2128)
```

## 테스트

```bash
source .venv/bin/activate
pytest tests/ -v
```

## 의존성

- numpy
- scipy
- Pillow
- pytest (dev)
