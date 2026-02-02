#!/usr/bin/env python3
"""MAT file viewer - PyQt6 based."""

import sys

from PyQt6.QtWidgets import QApplication

from src.main_window import MainWindow


def main():
    app = QApplication(sys.argv)
    app.setApplicationName("MAT Viewer")

    window = MainWindow()
    window.show()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
