"""Image registration module using phase correlation."""

import numpy as np
from typing import List, Tuple
from skimage.registration import phase_cross_correlation
from scipy.ndimage import shift as ndi_shift


def compute_shift(reference: np.ndarray, target: np.ndarray) -> Tuple[float, float]:
    """
    Compute x,y shift between reference and target using phase correlation.

    Args:
        reference: Reference image (grayscale or RGB).
        target: Target image to align (same shape as reference).

    Returns:
        Tuple of (dx, dy) shift values. Positive dx means target is shifted right,
        positive dy means target is shifted down.
    """
    # Convert to grayscale if needed
    ref_gray = _to_grayscale(reference)
    target_gray = _to_grayscale(target)

    # Compute phase correlation
    shift_yx, error, diffphase = phase_cross_correlation(
        ref_gray, target_gray, upsample_factor=10
    )

    # Return as (dx, dy) - note phase_cross_correlation returns (dy, dx)
    return (shift_yx[1], shift_yx[0])


def apply_shift(image: np.ndarray, dx: float, dy: float) -> np.ndarray:
    """
    Apply translation shift to an image.

    Args:
        image: Input image (grayscale or RGB).
        dx: Horizontal shift (positive = shift right).
        dy: Vertical shift (positive = shift down).

    Returns:
        Shifted image with same shape as input.
    """
    # ndi_shift expects shift in (y, x) order for 2D
    if image.ndim == 2:
        return ndi_shift(image, shift=(-dy, -dx), mode='constant', cval=0)
    elif image.ndim == 3:
        # For RGB, shift each channel
        shifted = np.zeros_like(image)
        for c in range(image.shape[2]):
            shifted[:, :, c] = ndi_shift(
                image[:, :, c], shift=(-dy, -dx), mode='constant', cval=0
            )
        return shifted
    else:
        raise ValueError(f"Unsupported image dimensions: {image.ndim}")


def register_channels(
    channels: List[np.ndarray], ref_index: int = 7
) -> Tuple[List[np.ndarray], List[Tuple[float, float]]]:
    """
    Register all channels to a reference channel using phase correlation.

    Args:
        channels: List of 15 channel images (numpy arrays).
        ref_index: Index of the reference channel (default: 7, center of 3x5 grid).

    Returns:
        Tuple of:
            - List of registered channel images
            - List of (dx, dy) shift values for each channel
    """
    if not channels:
        return [], []

    reference = channels[ref_index]
    registered = []
    shifts = []

    for i, channel in enumerate(channels):
        if i == ref_index:
            # Reference channel has no shift
            registered.append(channel.copy())
            shifts.append((0.0, 0.0))
        else:
            # Compute and apply shift
            dx, dy = compute_shift(reference, channel)
            shifted = apply_shift(channel, dx, dy)
            # Ensure output is uint8 for display
            shifted = np.clip(shifted, 0, 255).astype(np.uint8)
            registered.append(shifted)
            shifts.append((dx, dy))

    return registered, shifts


def _to_grayscale(image: np.ndarray) -> np.ndarray:
    """Convert image to grayscale if needed."""
    if image.ndim == 2:
        return image.astype(np.float64)
    elif image.ndim == 3:
        # Use standard luminance weights
        return np.dot(image[..., :3], [0.2989, 0.5870, 0.1140])
    else:
        raise ValueError(f"Unsupported image dimensions: {image.ndim}")
