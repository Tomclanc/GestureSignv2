from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
DAEMON_RESOURCES = ROOT / "GestureSign.Daemon" / "Resources"
CONTROL_PANEL_RESOURCES = ROOT / "GestureSign.ControlPanel" / "Resources"
PREVIEW_DIR = ROOT / "publish" / "icon-preview"
LOGO_PATH = ROOT / "tools" / "logo.png"

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def font(size):
    candidates = [
        Path(r"C:\Windows\Fonts\seguisb.ttf"),
        Path(r"C:\Windows\Fonts\segoeuib.ttf"),
        Path(r"C:\Windows\Fonts\arialbd.ttf"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default()


def rounded_square(size, color):
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    margin = max(0, round(size * 0.02))
    radius = max(3, round(size * 0.18))
    draw.rounded_rectangle([margin, margin, size - margin, size - margin], radius=radius, fill=color)
    draw.rounded_rectangle([margin, margin, size - margin, size - margin], radius=radius, outline=(255, 255, 255, 42), width=max(1, size // 64))
    return image


def draw_center_text(image, text, scale=0.72, y_adjust=0):
    size = image.size[0]
    draw = ImageDraw.Draw(image)
    fnt = font(max(8, round(size * scale)))
    bbox = draw.textbbox((0, 0), text, font=fnt)
    x = (size - (bbox[2] - bbox[0])) / 2 - bbox[0]
    y = (size - (bbox[3] - bbox[1])) / 2 - bbox[1] + y_adjust * size
    draw.text((x, y), text, font=fnt, fill=(255, 255, 255, 255))


def draw_minus(image):
    size = image.size[0]
    draw = ImageDraw.Draw(image)
    width = max(2, round(size * 0.13))
    length = round(size * 0.46)
    x1 = (size - length) // 2
    y1 = (size - width) // 2
    draw.rounded_rectangle([x1, y1, x1 + length, y1 + width], radius=width // 2, fill=(255, 255, 255, 255))


def icon_frames(kind):
    frames = []
    for size in SIZES:
        if kind == "normal":
            image = logo_frame(size)
        elif kind == "stop":
            image = logo_frame(size)
        elif kind == "add":
            image = logo_frame(size)
        else:
            raise ValueError(kind)
        frames.append(image)
    return frames


def logo_frame(size):
    source = Image.open(LOGO_PATH).convert("RGBA")
    alpha_bbox = source.getbbox()
    if alpha_bbox:
        source = source.crop(alpha_bbox)

    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    target = max(1, round(size * 0.96))
    scale = min(target / source.width, target / source.height)
    resized = source.resize((max(1, round(source.width * scale)), max(1, round(source.height * scale))), Image.Resampling.LANCZOS)
    x = (size - resized.width) // 2
    y = (size - resized.height) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas


def save_icon(path, kind):
    frames = icon_frames(kind)
    frames[-1].save(path, sizes=[(s, s) for s in SIZES], append_images=frames[:-1])


def save_preview(path, kind):
    frames = icon_frames(kind)
    canvas = Image.new("RGBA", (360, 112), (250, 250, 250, 255))
    x = 12
    for frame in [frames[0], frames[3], frames[5], frames[-1].resize((80, 80), Image.Resampling.LANCZOS)]:
        canvas.alpha_composite(frame.resize((80, 80), Image.Resampling.NEAREST if frame.size[0] <= 32 else Image.Resampling.LANCZOS), (x, 16))
        x += 88
    canvas.save(path)


def main():
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
    save_icon(DAEMON_RESOURCES / "normal_daemon.ico", "normal")
    save_icon(DAEMON_RESOURCES / "normal.ico", "normal")
    save_icon(CONTROL_PANEL_RESOURCES / "normal.ico", "normal")
    save_icon(DAEMON_RESOURCES / "stop.ico", "stop")
    save_icon(DAEMON_RESOURCES / "add.ico", "add")

    save_preview(PREVIEW_DIR / "normal.png", "normal")
    save_preview(PREVIEW_DIR / "stop.png", "stop")
    save_preview(PREVIEW_DIR / "add.png", "add")


if __name__ == "__main__":
    main()
