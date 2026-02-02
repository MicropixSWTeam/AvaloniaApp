"""BTES 방향성 보간 기반 2× 업샘플링 (논문 Eq. 18-21 응용)"""

import numpy as np


def btes_upsample(delta: np.ndarray, ppi_2x: np.ndarray, eps: float = 1e-6) -> np.ndarray:
    """Spectral difference를 BTES 방식으로 2× 업샘플.

    Args:
        delta: spectral difference (H, W)
        ppi_2x: 업스케일된 PPI (2H, 2W) - 가중치 계산용
        eps: division by zero 방지

    Returns:
        delta_2x (2H, 2W)
    """
    H, W = delta.shape
    H2, W2 = H * 2, W * 2

    # Step 0: 초기화 - 원본 delta를 짝수,짝수 위치에 배치
    delta_2x = np.zeros((H2, W2), dtype=np.float32)
    delta_2x[0::2, 0::2] = delta

    # Step 1: 대각선 보간 (홀수,홀수 위치)
    _interpolate_diagonal(delta_2x, ppi_2x, H, W, eps)

    # Step 2a: 십자 보간 (짝수,홀수 위치) - 수평 에지
    _interpolate_horizontal_edge(delta_2x, ppi_2x, H, W, eps)

    # Step 2b: 십자 보간 (홀수,짝수 위치) - 수직 에지
    _interpolate_vertical_edge(delta_2x, ppi_2x, H, W, eps)

    # Step 3: 경계 처리 - 마지막 행/열 복사
    _fill_boundary(delta_2x, H, W)

    return delta_2x


def _fill_boundary(delta_2x: np.ndarray, H: int, W: int):
    """경계 픽셀 처리 - 이웃 값으로 채움."""
    H2, W2 = H * 2, W * 2

    # 마지막 열 (홀수 열 인덱스 W2-1)이 0인 경우 이웃에서 복사
    # (짝수,홀수) 위치에서 마지막 열은 j=W-1일 때 x=2*(W-1)+1=2W-1=W2-1
    # 하지만 horizontal_edge 루프는 j < W-1 까지만 처리하므로 W2-1 열은 미처리
    # 마지막 열은 짝수 위치(원본)만 있고 홀수 위치가 비어있음
    for i in range(H2):
        if i % 2 == 0:
            # (짝수, W2-1): 홀수 열이므로 좌측에서 복사
            if W2 - 1 > 0:
                delta_2x[i, W2 - 1] = delta_2x[i, W2 - 2]
        else:
            # (홀수, W2-1): 홀수 열이므로 좌측에서 복사
            if W2 - 1 > 0:
                delta_2x[i, W2 - 1] = delta_2x[i, W2 - 2]

    # 마지막 행도 동일하게 처리
    for j in range(W2):
        if j % 2 == 0:
            # (H2-1, 짝수): 홀수 행이므로 상단에서 복사
            if H2 - 1 > 0:
                delta_2x[H2 - 1, j] = delta_2x[H2 - 2, j]
        else:
            # (H2-1, 홀수): 이미 대각선 또는 이전 단계에서 처리되었을 수 있음
            if H2 - 1 > 0:
                delta_2x[H2 - 1, j] = delta_2x[H2 - 2, j]


def _interpolate_diagonal(delta_2x: np.ndarray, ppi_2x: np.ndarray,
                          H: int, W: int, eps: float):
    """Step 1: 대각선 보간 (홀수,홀수) - Eq. 18-19 응용.

    [NW] .  [NE]
      .  [X]  .
    [SW] .  [SE]

    가중치: γ_NW = 1 / (2|ppi_NW - ppi_center| + |ppi_NW - ppi_SE| + ε)
    """
    for i in range(H - 1):
        for j in range(W - 1):
            y, x = 2*i + 1, 2*j + 1  # target: 홀수,홀수

            # 4개의 대각선 이웃 delta 값 (원본 위치)
            d_nw = delta_2x[2*i, 2*j]
            d_ne = delta_2x[2*i, 2*j + 2]
            d_se = delta_2x[2*i + 2, 2*j + 2]
            d_sw = delta_2x[2*i + 2, 2*j]

            # PPI 값들
            p_c = ppi_2x[y, x]  # center
            p_nw = ppi_2x[2*i, 2*j]
            p_ne = ppi_2x[2*i, 2*j + 2]
            p_se = ppi_2x[2*i + 2, 2*j + 2]
            p_sw = ppi_2x[2*i + 2, 2*j]

            # 논문 Eq.19 스타일 가중치: 1/(2*dist + gradient + eps)
            g_nw = 1.0 / (2*abs(p_nw - p_c) + abs(p_nw - p_se) + eps)
            g_ne = 1.0 / (2*abs(p_ne - p_c) + abs(p_ne - p_sw) + eps)
            g_se = 1.0 / (2*abs(p_se - p_c) + abs(p_se - p_nw) + eps)
            g_sw = 1.0 / (2*abs(p_sw - p_c) + abs(p_sw - p_ne) + eps)

            total = g_nw + g_ne + g_se + g_sw
            delta_2x[y, x] = (g_nw*d_nw + g_ne*d_ne + g_se*d_se + g_sw*d_sw) / total


def _interpolate_horizontal_edge(delta_2x: np.ndarray, ppi_2x: np.ndarray,
                                  H: int, W: int, eps: float):
    """Step 2a: (짝수,홀수) 위치 - 좌우 원본 + 상하 Step1 결과 사용.

    상하로 Step1 결과, 좌우로 원본 delta.
         [N?]
    [W] -- X -- [E]
         [S?]
    """
    for i in range(H):
        for j in range(W - 1):
            y, x = 2*i, 2*j + 1  # target: 짝수,홀수

            # 좌우 이웃 (원본 delta)
            d_w = delta_2x[y, 2*j]
            d_e = delta_2x[y, 2*j + 2]

            p_c = ppi_2x[y, x]
            p_w = ppi_2x[y, 2*j]
            p_e = ppi_2x[y, 2*j + 2]

            g_w = 1.0 / (2*abs(p_w - p_c) + abs(p_w - p_e) + eps)
            g_e = 1.0 / (2*abs(p_e - p_c) + abs(p_e - p_w) + eps)

            weighted_sum = g_w*d_w + g_e*d_e
            total_g = g_w + g_e

            # 상하 이웃 (Step 1 결과, 경계 체크)
            if i > 0:
                d_n = delta_2x[y - 1, x]  # 홀수,홀수 위치 (Step1)
                p_n = ppi_2x[y - 1, x]
                p_s_ref = ppi_2x[y + 1, x] if i < H - 1 else p_c
                g_n = 1.0 / (2*abs(p_n - p_c) + abs(p_n - p_s_ref) + eps)
                weighted_sum += g_n * d_n
                total_g += g_n

            if i < H - 1:
                d_s = delta_2x[y + 1, x]  # 홀수,홀수 위치 (Step1)
                p_s = ppi_2x[y + 1, x]
                p_n_ref = ppi_2x[y - 1, x] if i > 0 else p_c
                g_s = 1.0 / (2*abs(p_s - p_c) + abs(p_s - p_n_ref) + eps)
                weighted_sum += g_s * d_s
                total_g += g_s

            delta_2x[y, x] = weighted_sum / total_g


def _interpolate_vertical_edge(delta_2x: np.ndarray, ppi_2x: np.ndarray,
                                H: int, W: int, eps: float):
    """Step 2b: (홀수,짝수) 위치 - 상하 원본 + 좌우 Step1 결과 사용.

         [N]
    [W?]--X--[E?]
         [S]
    """
    for i in range(H - 1):
        for j in range(W):
            y, x = 2*i + 1, 2*j  # target: 홀수,짝수

            # 상하 이웃 (원본 delta)
            d_n = delta_2x[2*i, x]
            d_s = delta_2x[2*i + 2, x]

            p_c = ppi_2x[y, x]
            p_n = ppi_2x[2*i, x]
            p_s = ppi_2x[2*i + 2, x]

            g_n = 1.0 / (2*abs(p_n - p_c) + abs(p_n - p_s) + eps)
            g_s = 1.0 / (2*abs(p_s - p_c) + abs(p_s - p_n) + eps)

            weighted_sum = g_n*d_n + g_s*d_s
            total_g = g_n + g_s

            # 좌우 이웃 (Step 1 결과, 경계 체크)
            if j > 0:
                d_w = delta_2x[y, x - 1]  # 홀수,홀수 위치 (Step1)
                p_w = ppi_2x[y, x - 1]
                p_e_ref = ppi_2x[y, x + 1] if j < W - 1 else p_c
                g_w = 1.0 / (2*abs(p_w - p_c) + abs(p_w - p_e_ref) + eps)
                weighted_sum += g_w * d_w
                total_g += g_w

            if j < W - 1:
                d_e = delta_2x[y, x + 1]  # 홀수,홀수 위치 (Step1)
                p_e = ppi_2x[y, x + 1]
                p_w_ref = ppi_2x[y, x - 1] if j > 0 else p_c
                g_e = 1.0 / (2*abs(p_e - p_c) + abs(p_e - p_w_ref) + eps)
                weighted_sum += g_e * d_e
                total_g += g_e

            delta_2x[y, x] = weighted_sum / total_g
