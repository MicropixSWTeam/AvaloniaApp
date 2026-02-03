"""Image splitter module for 3x5 tiled images."""

import numpy as np
from PIL import Image
from typing import List, Tuple


def split_image(image_path: str) -> Tuple[List[np.ndarray], Tuple[int, int]]:
    """
    Split a 3x5 tiled image into 15 individual channel images.

    Args:
        image_path: Path to the input image file.

    Returns:
        Tuple of (list of 15 numpy arrays, tile size as (height, width)).
        Channels are ordered row-by-row (0-4: row 0, 5-9: row 1, 10-14: row 2).
    """
    img = Image.open(image_path)
    img_array = np.array(img)

    height, width = img_array.shape[:2]
    tile_height = height // 3
    tile_width = width // 5

    channels = []
    for row in range(3):
        for col in range(5):
            y_start = row * tile_height
            y_end = (row + 1) * tile_height
            x_start = col * tile_width
            x_end = (col + 1) * tile_width

            tile = img_array[y_start:y_end, x_start:x_end]
            channels.append(tile)

    return channels, (tile_height, tile_width)


def get_channel_label(index: int) -> str:
    """
    Get a descriptive label for a channel.

    Args:
        index: Channel index (0-14).

    Returns:
        Label string like "Channel 0 (Row 0, Col 0)".
    """
    row = index // 5
    col = index % 5
    return f"Channel {index} (Row {row}, Col {col})"
