"""메인 윈도우"""
from PyQt6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QSlider, QSpinBox, QLabel, QPushButton, QFileDialog
)
from PyQt6.QtCore import Qt
from .image_view import ImageView
from .mat_loader import MatLoader


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.loader = None
        self.setup_ui()

    def setup_ui(self):
        self.setWindowTitle("MAT Viewer")
        self.setMinimumSize(800, 700)

        central = QWidget()
        layout = QVBoxLayout(central)

        # 상단: Open 버튼
        top_layout = QHBoxLayout()
        self.open_btn = QPushButton("Open MAT")
        self.open_btn.clicked.connect(self.open_file)
        self.file_label = QLabel("No file loaded")
        top_layout.addWidget(self.open_btn)
        top_layout.addWidget(self.file_label, 1)
        layout.addLayout(top_layout)

        # 중앙: 이미지 뷰
        self.image_view = ImageView()
        layout.addWidget(self.image_view, 1)

        # 하단: 채널 선택
        bottom_layout = QHBoxLayout()
        bottom_layout.addWidget(QLabel("Channel:"))

        self.channel_slider = QSlider(Qt.Orientation.Horizontal)
        self.channel_slider.setEnabled(False)
        self.channel_slider.valueChanged.connect(self.on_channel_changed)
        bottom_layout.addWidget(self.channel_slider, 1)

        self.channel_spin = QSpinBox()
        self.channel_spin.setEnabled(False)
        self.channel_spin.valueChanged.connect(self.channel_slider.setValue)
        bottom_layout.addWidget(self.channel_spin)

        layout.addLayout(bottom_layout)

        # 통계 라벨
        self.stats_label = QLabel("")
        layout.addWidget(self.stats_label)

        self.setCentralWidget(central)

    def open_file(self):
        path, _ = QFileDialog.getOpenFileName(
            self, "Open MAT File", "", "MAT Files (*.mat)"
        )
        if path:
            self.load_mat(path)

    def load_mat(self, path: str):
        self.loader = MatLoader(path)
        self.loader.load()

        n = self.loader.n_channels
        self.channel_slider.setRange(0, n - 1)
        self.channel_slider.setEnabled(True)
        self.channel_spin.setRange(0, n - 1)
        self.channel_spin.setEnabled(True)

        self.file_label.setText(f"{path} ({n} channels)")
        self.channel_slider.setValue(0)
        # Force update even if already at 0
        self.on_channel_changed(0)

    def on_channel_changed(self, idx: int):
        if self.loader is None:
            return
        self.channel_spin.blockSignals(True)
        self.channel_spin.setValue(idx)
        self.channel_spin.blockSignals(False)

        ch = self.loader.get_channel(idx)
        self.image_view.set_image(ch)

        stats = self.loader.get_channel_stats(idx)
        self.stats_label.setText(
            f"Channel {idx}: min={stats['min']:.4f}, max={stats['max']:.4f}, "
            f"mean={stats['mean']:.4f}, std={stats['std']:.4f}"
        )
