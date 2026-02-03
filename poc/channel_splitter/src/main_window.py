"""Main window with drag and drop support for channel splitting."""

from PyQt6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QScrollArea, QLabel
)
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QDragEnterEvent, QDropEvent

from .image_splitter import split_image, get_channel_label
from .channel_view import ChannelView


class MainWindow(QMainWindow):
    """Main application window with drag and drop image loading."""

    def __init__(self):
        super().__init__()
        self._setup_ui()
        self.setAcceptDrops(True)

    def _setup_ui(self):
        """Set up the main window UI."""
        self.setWindowTitle("Channel Splitter - 3x5 Tile Viewer")
        self.setMinimumSize(600, 800)

        # Central widget with scroll area
        central_widget = QWidget()
        self.setCentralWidget(central_widget)

        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(0, 0, 0, 0)

        # Scroll area for channels
        self.scroll_area = QScrollArea()
        self.scroll_area.setWidgetResizable(True)
        self.scroll_area.setHorizontalScrollBarPolicy(
            Qt.ScrollBarPolicy.ScrollBarAsNeeded
        )
        self.scroll_area.setVerticalScrollBarPolicy(
            Qt.ScrollBarPolicy.ScrollBarAsNeeded
        )
        main_layout.addWidget(self.scroll_area)

        # Container widget for channels
        self.channel_container = QWidget()
        self.channel_layout = QVBoxLayout(self.channel_container)
        self.channel_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self.scroll_area.setWidget(self.channel_container)

        # Initial placeholder
        self._show_placeholder()

    def _show_placeholder(self):
        """Show placeholder text when no image is loaded."""
        self._clear_channels()

        placeholder = QLabel(
            "Drag and drop a 3x5 tiled image here\n\n"
            "The image will be split into 15 channels\n"
            "and displayed vertically for scrolling"
        )
        placeholder.setAlignment(Qt.AlignmentFlag.AlignCenter)
        placeholder.setStyleSheet("""
            QLabel {
                font-size: 16px;
                color: #666;
                padding: 50px;
                border: 2px dashed #ccc;
                border-radius: 10px;
                margin: 20px;
            }
        """)
        self.channel_layout.addWidget(placeholder)

    def _clear_channels(self):
        """Remove all widgets from the channel layout."""
        while self.channel_layout.count():
            item = self.channel_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

    def _load_image(self, file_path: str):
        """Load and split an image file."""
        try:
            channels, tile_size = split_image(file_path)
            self._display_channels(channels)
            self.setWindowTitle(
                f"Channel Splitter - {file_path} "
                f"(Tile: {tile_size[1]}x{tile_size[0]})"
            )
        except Exception as e:
            self._show_error(str(e))

    def _display_channels(self, channels):
        """Display all channels in the scroll area."""
        self._clear_channels()

        for i, channel_data in enumerate(channels):
            label = get_channel_label(i)
            channel_view = ChannelView(channel_data, label)
            self.channel_layout.addWidget(channel_view)

        # Add stretch at the end
        self.channel_layout.addStretch()

    def _show_error(self, message: str):
        """Show an error message."""
        self._clear_channels()

        error_label = QLabel(f"Error: {message}\n\nPlease try another image.")
        error_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        error_label.setStyleSheet("""
            QLabel {
                font-size: 14px;
                color: #c00;
                padding: 30px;
            }
        """)
        self.channel_layout.addWidget(error_label)

    def dragEnterEvent(self, event: QDragEnterEvent):
        """Handle drag enter event."""
        if event.mimeData().hasUrls():
            urls = event.mimeData().urls()
            if urls and urls[0].isLocalFile():
                file_path = urls[0].toLocalFile().lower()
                if file_path.endswith(('.png', '.jpg', '.jpeg', '.bmp', '.tiff', '.tif')):
                    event.acceptProposedAction()
                    return
        event.ignore()

    def dropEvent(self, event: QDropEvent):
        """Handle drop event."""
        urls = event.mimeData().urls()
        if urls and urls[0].isLocalFile():
            file_path = urls[0].toLocalFile()
            self._load_image(file_path)
            event.acceptProposedAction()
