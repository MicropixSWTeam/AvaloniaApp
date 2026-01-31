#!/usr/bin/env python3
"""CLI entry point for PPI generation."""

import argparse
from pathlib import Path

import numpy as np
from PIL import Image

from src import PPISimple, PPIPPID, PPIIGFPPI, GuidedUpsampler


METHODS = {
    "simple": PPISimple,
    "ppid": PPIPPID,
    "igfppi": PPIIGFPPI,
}

UPSCALE_METHODS = ["guided", "bicubic", "lanczos"]


def main():
    parser = argparse.ArgumentParser(
        description="Generate Pseudo-Panchromatic Image (PPI) from multispectral channels"
    )
    parser.add_argument(
        "--input",
        "-i",
        type=Path,
        default=Path("data"),
        help="Input directory containing *nm.png files (default: data)",
    )
    parser.add_argument(
        "--output",
        "-o",
        type=Path,
        default=Path("output/ppi_result.png"),
        help="Output PPI file path (default: output/ppi_result.png)",
    )
    parser.add_argument(
        "--method",
        "-m",
        choices=list(METHODS.keys()),
        default="simple",
        help="PPI generation method (default: simple)",
    )
    parser.add_argument(
        "--upscale",
        type=int,
        default=None,
        help="Upscale factor (e.g., 2 for 2x upscaling)",
    )
    parser.add_argument(
        "--upscale-method",
        choices=UPSCALE_METHODS,
        default="guided",
        help="Upscaling method (default: guided)",
    )

    args = parser.parse_args()

    print(f"Input directory: {args.input}")
    print(f"Output file: {args.output}")
    print(f"Method: {args.method}")
    if args.upscale:
        print(f"Upscale: {args.upscale}x ({args.upscale_method})")
    print("-" * 40)

    generator_cls = METHODS[args.method]
    generator = generator_cls(args.input)

    print("Loading channels...")
    channels = generator.load_channels()
    print(f"Loaded {len(channels)} channels, shape: {channels.shape[1:]} each")

    print(f"Generating PPI ({args.method})...")
    ppi = generator.generate_ppi()

    # Apply upscaling if requested
    if args.upscale:
        print(f"Upscaling {args.upscale}x using {args.upscale_method}...")
        upscaler = GuidedUpsampler(scale_factor=args.upscale, method=args.upscale_method)
        # Pass raw MSFA channels for guided upsampling
        ppi = upscaler.upscale(ppi, channels=channels)
        # Update generator's ppi for statistics
        generator.ppi = ppi

    print("Saving PPI...")
    saved_path = generator.save_ppi(args.output)
    print(f"Saved to: {saved_path}")

    print("-" * 40)
    print("Statistics:")
    stats = generator.get_statistics()
    print(f"  Method: {stats['method']}")
    print(f"  Shape: {stats['shape']}")
    print(f"  Channels: {stats['num_channels']}")
    print(f"  Mean: {stats['mean']:.2f}")
    print(f"  Std:  {stats['std']:.2f}")
    print(f"  Min:  {stats['min']:.2f}")
    print(f"  Max:  {stats['max']:.2f}")


if __name__ == "__main__":
    main()
