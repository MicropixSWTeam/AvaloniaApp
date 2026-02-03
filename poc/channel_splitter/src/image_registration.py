"""Image registration module using template matching."""

import numpy as np
from typing import List, Tuple
from scipy.ndimage import shift as ndi_shift
from scipy.signal import correlate2d
from concurrent.futures import ProcessPoolExecutor, as_completed


def compute_shift(reference: np.ndarray, target: np.ndarray) -> Tuple[float, float]:
    """
    Compute x,y shift using template matching on center region.

    Args:
        reference: Reference image (grayscale or RGB).
        target: Target image to align (same shape as reference).

    Returns:
        Tuple of (dx, dy) shift values.
    """
    ref_gray = _to_grayscale(reference)
    target_gray = _to_grayscale(target)

    # Extract center region as template (1/3 of image size)
    h, w = ref_gray.shape
    th, tw = h // 3, w // 3
    cy, cx = h // 2, w // 2
    template = ref_gray[cy - th//2 : cy + th//2, cx - tw//2 : cx + tw//2]

    # Normalize template
    template = template - template.mean()

    # Search in a larger region of target
    search_margin = max(h, w) // 4
    sy1 = max(0, cy - th//2 - search_margin)
    sy2 = min(h, cy + th//2 + search_margin)
    sx1 = max(0, cx - tw//2 - search_margin)
    sx2 = min(w, cx + tw//2 + search_margin)
    search_region = target_gray[sy1:sy2, sx1:sx2]
    search_region = search_region - search_region.mean()

    # Cross-correlation
    corr = correlate2d(search_region, template, mode='same')

    # Find peak
    peak_y, peak_x = np.unravel_index(np.argmax(corr), corr.shape)

    # Calculate shift relative to center of search region
    expected_y = (sy2 - sy1) // 2
    expected_x = (sx2 - sx1) // 2

    dy = peak_y - expected_y
    dx = peak_x - expected_x

    return (float(dx), float(dy))


def apply_shift(image: np.ndarray, dx: float, dy: float, highlight_empty: bool = True) -> np.ndarray:
    """
    Apply translation shift to an image.

    Args:
        image: Input image (grayscale or RGB).
        dx: Horizontal shift (positive = shift right).
        dy: Vertical shift (positive = shift down).
        highlight_empty: If True, fill empty areas with red. If False, fill with black.

    Returns:
        Shifted RGB image.
    """
    # Convert grayscale to RGB first
    if image.ndim == 2:
        rgb_image = np.stack([image, image, image], axis=-1)
    else:
        rgb_image = image.copy()

    height, width = rgb_image.shape[:2]

    # Calculate empty regions based on shift
    shift_y, shift_x = int(round(-dy)), int(round(-dx))

    # Apply shift to each channel
    shifted = np.zeros_like(rgb_image)
    for c in range(3):
        shifted[:, :, c] = ndi_shift(
            rgb_image[:, :, c], shift=(shift_y, shift_x), mode='constant', cval=0
        )

    # Fill empty areas with red if highlighting
    if highlight_empty:
        empty_mask = np.zeros((height, width), dtype=bool)

        if shift_y > 0:
            empty_mask[:shift_y, :] = True
        elif shift_y < 0:
            empty_mask[shift_y:, :] = True

        if shift_x > 0:
            empty_mask[:, :shift_x] = True
        elif shift_x < 0:
            empty_mask[:, shift_x:] = True

        shifted[empty_mask, 0] = 255  # Red
        shifted[empty_mask, 1] = 0    # Green
        shifted[empty_mask, 2] = 0    # Blue

    return shifted


def _process_channel(args) -> Tuple[int, np.ndarray, Tuple[float, float]]:
    """Process a single channel for registration (for parallel execution)."""
    i, channel, reference, is_ref = args

    if is_ref:
        # Reference channel - convert to RGB
        if channel.ndim == 2:
            rgb = np.stack([channel, channel, channel], axis=-1)
        else:
            rgb = channel.copy()
        return (i, rgb, (0.0, 0.0))
    else:
        # Compute and apply shift
        dx, dy = compute_shift(reference, channel)
        shifted = apply_shift(channel, dx, dy)
        shifted = np.clip(shifted, 0, 255).astype(np.uint8)
        return (i, shifted, (dx, dy))


def register_channels(
    channels: List[np.ndarray], ref_index: int = 7
) -> Tuple[List[np.ndarray], List[Tuple[float, float]]]:
    """
    Register all channels to a reference channel using template matching.
    Uses parallel processing for speed.

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
    total = len(channels)

    # Prepare arguments for parallel processing
    args_list = [
        (i, channels[i], reference, i == ref_index)
        for i in range(total)
    ]

    # Process in parallel
    print(f"Registering {total} channels in parallel...", flush=True)
    results = [None] * total

    with ProcessPoolExecutor() as executor:
        futures = {executor.submit(_process_channel, args): args[0] for args in args_list}

        for future in as_completed(futures):
            idx, shifted, shift = future.result()
            results[idx] = (shifted, shift)
            dx, dy = shift
            if idx == ref_index:
                print(f"  Channel {idx+1}/{total}: (reference)", flush=True)
            else:
                print(f"  Channel {idx+1}/{total}: dx={dx:+.1f}, dy={dy:+.1f}", flush=True)

    registered = [r[0] for r in results]
    shifts = [r[1] for r in results]

    print("Registration complete!", flush=True)

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


# Channel wavelengths for export filenames (410nm to 690nm, 20nm steps)
WAVELENGTHS = [410, 430, 450, 470, 490, 510, 530, 550, 570, 590, 610, 630, 650, 670, 690]


def export_registered_channels(
    channels: List[np.ndarray],
    shifts: List[Tuple[float, float]],
    output_dir: str
) -> List[str]:
    """
    Export registered channels as grayscale PNG files with black fill.

    Args:
        channels: List of original channel images.
        shifts: List of (dx, dy) shift values for each channel.
        output_dir: Directory to save the PNG files.

    Returns:
        List of saved file paths.
    """
    import os
    from PIL import Image

    os.makedirs(output_dir, exist_ok=True)

    saved_files = []
    total = len(channels)

    print(f"Exporting {total} channels to {output_dir}...", flush=True)

    for i, (channel, (dx, dy)) in enumerate(zip(channels, shifts)):
        # Apply shift with black fill (no highlight)
        shifted = apply_shift(channel, dx, dy, highlight_empty=False)
        shifted = np.clip(shifted, 0, 255).astype(np.uint8)

        # Convert to grayscale for export
        if shifted.ndim == 3:
            gray = np.dot(shifted[..., :3], [0.2989, 0.5870, 0.1140]).astype(np.uint8)
        else:
            gray = shifted

        # Save as PNG
        wavelength = WAVELENGTHS[i] if i < len(WAVELENGTHS) else (410 + i * 20)
        filename = f"{wavelength}nm.png"
        filepath = os.path.join(output_dir, filename)

        Image.fromarray(gray).save(filepath)
        saved_files.append(filepath)
        print(f"  Saved {filename}", flush=True)

    print("Export complete!", flush=True)
    return saved_files
