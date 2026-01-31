#!/usr/bin/env python3
"""CLI entry point for PPI generation pipeline."""

import argparse
from pathlib import Path

import numpy as np
from PIL import Image

from src import PPISimple, PPIPPID, PPIIGFPPI, GuidedUpsampler, SpectralUpsampler


METHODS = {
    "simple": PPISimple,
    "ppid": PPIPPID,
    "igfppi": PPIIGFPPI,
}

UPSCALE_METHODS = ["guided", "bicubic", "lanczos"]


def save_image(arr: np.ndarray, path: Path) -> Path:
    """Save numpy array as grayscale PNG."""
    path.parent.mkdir(parents=True, exist_ok=True)
    img_uint8 = np.clip(arr, 0, 255).astype(np.uint8)
    img = Image.fromarray(img_uint8, mode="L")
    img.save(path)
    return path


def save_normalized_image(arr: np.ndarray, path: Path) -> Path:
    """Save numpy array as normalized grayscale PNG (0-255 range)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    arr_min, arr_max = arr.min(), arr.max()
    if arr_max > arr_min:
        arr_norm = (arr - arr_min) / (arr_max - arr_min) * 255
    else:
        arr_norm = np.zeros_like(arr)
    img_uint8 = arr_norm.astype(np.uint8)
    img = Image.fromarray(img_uint8, mode="L")
    img.save(path)
    return path


def run_pipeline(args):
    """Run the full PPI generation and upscaling pipeline."""
    output_dir = args.output_dir
    output_dir.mkdir(parents=True, exist_ok=True)

    print(f"Input directory: {args.input}")
    print(f"Output directory: {output_dir}")
    print(f"PPI method: {args.method}")
    print(f"Upscale: {args.upscale}x ({args.upscale_method})")
    print("=" * 50)

    # Step 1: Load channels
    print("\n[Step 1] Loading MSFA channels...")
    generator_cls = METHODS[args.method]
    generator = generator_cls(args.input)
    channels = generator.load_channels()
    print(f"  Loaded {len(channels)} channels, shape: {channels.shape[1:]} each")

    # Step 2: Generate PPI
    print(f"\n[Step 2] Generating PPI ({args.method})...")
    ppi = generator.generate_ppi()
    ppi_path = output_dir / f"1_ppi_{args.method}.png"
    save_image(ppi, ppi_path)
    print(f"  Shape: {ppi.shape}")
    print(f"  Saved: {ppi_path}")

    # Step 3: Compute guide from MSFA
    print("\n[Step 3] Computing guide from MSFA channels...")
    upscaler = GuidedUpsampler(scale_factor=args.upscale, method=args.upscale_method)
    guide = upscaler._compute_msfa_guide(channels)
    guide_path = output_dir / "2_guide_msfa.png"
    save_normalized_image(guide, guide_path)
    print(f"  Shape: {guide.shape}")
    print(f"  Saved: {guide_path}")

    # Step 4: Upscale PPI
    print(f"\n[Step 4] Upscaling PPI {args.upscale}x ({args.upscale_method})...")
    ppi_upscaled = upscaler.upscale(ppi, channels=channels)
    upscaled_path = output_dir / f"3_ppi_{args.method}_{args.upscale}x_{args.upscale_method}.png"
    save_image(ppi_upscaled, upscaled_path)
    print(f"  Shape: {ppi_upscaled.shape}")
    print(f"  Saved: {upscaled_path}")

    # Step 5: Upsample all channels
    print(f"\n[Step 5] Upsampling all channels...")
    spectral_upsampler = SpectralUpsampler()
    channels_2x = spectral_upsampler.upsample_all_channels(channels, ppi, ppi_upscaled)
    print(f"  Original: {channels.shape}")
    print(f"  Upscaled: {channels_2x.shape}")

    # Save sample channels
    channel_paths = []
    for i in [0, 7, 14]:  # 첫번째, 중간, 마지막 채널
        ch_path = output_dir / f"4_channel_{i}_2x.png"
        save_image(channels_2x[i], ch_path)
        channel_paths.append(ch_path)
        print(f"  Saved: {ch_path}")

    # Summary
    print("\n" + "=" * 50)
    print("Pipeline complete! Output files:")
    print(f"  1. PPI ({args.method}):     {ppi_path}")
    print(f"  2. Guide (MSFA):            {guide_path}")
    print(f"  3. Upscaled PPI ({args.upscale}x):    {upscaled_path}")
    print(f"  4. Upscaled channels:       {len(channels_2x)} channels")
    for ch_path in channel_paths:
        print(f"     - {ch_path}")

    # Statistics
    print("\n" + "-" * 50)
    print("Statistics:")
    print(f"  Original PPI:      {ppi.shape[0]}x{ppi.shape[1]}, mean={ppi.mean():.2f}, std={ppi.std():.2f}")
    print(f"  Upscaled PPI:      {ppi_upscaled.shape[0]}x{ppi_upscaled.shape[1]}, mean={ppi_upscaled.mean():.2f}, std={ppi_upscaled.std():.2f}")
    print(f"  Original channels: {channels.shape}")
    print(f"  Upscaled channels: {channels_2x.shape}")


def main():
    parser = argparse.ArgumentParser(
        description="PPI generation and upscaling pipeline"
    )
    parser.add_argument(
        "--input",
        "-i",
        type=Path,
        default=Path("data"),
        help="Input directory containing *nm.png files (default: data)",
    )
    parser.add_argument(
        "--output-dir",
        "-o",
        type=Path,
        default=Path("output"),
        help="Output directory for pipeline results (default: output)",
    )
    parser.add_argument(
        "--method",
        "-m",
        choices=list(METHODS.keys()),
        default="igfppi",
        help="PPI generation method (default: igfppi)",
    )
    parser.add_argument(
        "--upscale",
        type=int,
        default=2,
        help="Upscale factor (default: 2)",
    )
    parser.add_argument(
        "--upscale-method",
        choices=UPSCALE_METHODS,
        default="guided",
        help="Upscaling method (default: guided)",
    )

    args = parser.parse_args()
    run_pipeline(args)


if __name__ == "__main__":
    main()
