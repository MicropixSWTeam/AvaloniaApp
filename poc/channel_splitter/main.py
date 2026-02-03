#!/usr/bin/env python3
"""Channel Splitter - 3x5 Tile Image Viewer.

A simple viewer that splits 3x5 tiled images into 15 individual channels
and displays them in a vertically scrollable view.
"""

import sys
from PyQt6.QtWidgets import QApplication

from src.main_window import MainWindow


def main():
    """Application entry point."""
    app = QApplication(sys.argv)
    app.setApplicationName("Channel Splitter")

    window = MainWindow()
    window.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
