#!/usr/bin/env python3
"""CLI entry point for PPI generation."""

import argparse
from pathlib import Path

from src import PPISimple, PPIPPID, PPIIGFPPI


METHODS = {
    "simple": PPISimple,
    "ppid": PPIPPID,
    "igfppi": PPIIGFPPI,
}


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

    args = parser.parse_args()

    print(f"Input directory: {args.input}")
    print(f"Output file: {args.output}")
    print(f"Method: {args.method}")
    print("-" * 40)

    generator_cls = METHODS[args.method]
    generator = generator_cls(args.input)

    print("Loading channels...")
    channels = generator.load_channels()
    print(f"Loaded {len(channels)} channels, shape: {channels.shape[1:]} each")

    print(f"Generating PPI ({args.method})...")
    generator.generate_ppi()

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
