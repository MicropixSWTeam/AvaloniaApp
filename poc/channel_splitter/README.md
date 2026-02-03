# Channel Splitter

3x5 타일 이미지를 15개 채널로 분리하고, 중앙 채널 기준으로 registration을 수행하는 도구.

## 기능

- **이미지 분리**: 3x5 타일 이미지를 15개 개별 채널로 분리
- **Channel Registration**: 중앙 채널(index 7) 기준으로 x,y translation 정렬
- **Export**: 정렬된 채널을 개별 PNG 파일로 저장

## 알고리즘

### Template Matching

Reference 이미지의 중앙 영역을 템플릿으로 추출하고, 각 채널에서 Cross-correlation으로 템플릿 위치를 찾아 shift를 계산.

반복 패턴이 있는 이미지(USAF test chart 등)에서도 중앙의 고유한 패턴을 기준으로 정확하게 매칭됨.

## 사용법

```bash
cd poc/channel_splitter
pip install -r requirements.txt
python main.py
```

1. 3x5 타일 이미지를 창에 드래그 앤 드롭
2. **Register** 버튼 클릭 → 모든 채널 정렬
3. **Export** 버튼 클릭 → `ppi_upscale/data/`에 PNG로 저장

## 출력 파일

```
410nm.png, 430nm.png, ... 690nm.png (20nm 간격, 총 15개)
```

## 의존성

- PyQt6
- numpy
- Pillow
- scipy
