#!/usr/bin/env python3
import os
from PIL import Image, ImageDraw, ImageFont

BG_COLOR = (15, 118, 110) # #0F766E
TEXT_COLOR = (255, 255, 255) # White
FONT_PATH = "/usr/share/fonts/truetype/noto/NotoKufiArabic-ExtraBold.ttf"
if not os.path.exists(FONT_PATH):
    FONT_PATH = "/usr/share/fonts/truetype/noto/NotoSansArabic-Bold.ttf"

RES_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "../android/app/src/main/res"))

def create_icon(size, output_path, round_icon=False):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    if round_icon:
        draw.ellipse([0, 0, size - 1, size - 1], fill=BG_COLOR)
    else:
        # rounded rectangle
        radius = int(size * 0.22)
        draw.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=BG_COLOR)
    
    font_size = int(size * 0.58)
    font = ImageFont.truetype(FONT_PATH, font_size)
    
    # Measure and center letter 'ب'
    text = "ب"
    bbox = draw.textbbox((0, 0), text, font=font)
    text_w = bbox[2] - bbox[0]
    text_h = bbox[3] - bbox[1]
    x = (size - text_w) // 2 - bbox[0]
    y = (size - text_h) // 2 - bbox[1] - int(size * 0.04) # slightly adjust vertical center
    
    draw.text((x, y), text, font=font, fill=TEXT_COLOR)
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    img.save(output_path, "PNG")

def create_splash(width, height, output_path):
    img = Image.new("RGB", (width, height), BG_COLOR)
    draw = ImageDraw.Draw(img)
    
    # Center emblem
    mark_size = min(width, height) // 3
    font = ImageFont.truetype(FONT_PATH, mark_size)
    text = "ب"
    bbox = draw.textbbox((0, 0), text, font=font)
    text_w = bbox[2] - bbox[0]
    text_h = bbox[3] - bbox[1]
    x = (width - text_w) // 2 - bbox[0]
    y = (height - text_h) // 2 - bbox[1]
    
    draw.text((x, y), text, font=font, fill=TEXT_COLOR)
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    img.save(output_path, "PNG")

# Generate launcher icons
mipmap_sizes = {
    "mipmap-mdpi": 48,
    "mipmap-hdpi": 72,
    "mipmap-xhdpi": 96,
    "mipmap-xxhdpi": 144,
    "mipmap-xxxhdpi": 192,
}

for folder, size in mipmap_sizes.items():
    create_icon(size, os.path.join(RES_DIR, folder, "ic_launcher.png"), round_icon=False)
    create_icon(size, os.path.join(RES_DIR, folder, "ic_launcher_round.png"), round_icon=True)
    create_icon(size, os.path.join(RES_DIR, folder, "ic_launcher_foreground.png"), round_icon=False)

# Generate splash screens
splashes = [
    ("drawable/splash.png", 480, 800),
    ("drawable-port-mdpi/splash.png", 320, 480),
    ("drawable-port-hdpi/splash.png", 480, 800),
    ("drawable-port-xhdpi/splash.png", 720, 1280),
    ("drawable-port-xxhdpi/splash.png", 960, 1600),
    ("drawable-port-xxxhdpi/splash.png", 1280, 1920),
    ("drawable-land-mdpi/splash.png", 480, 320),
    ("drawable-land-hdpi/splash.png", 800, 480),
    ("drawable-land-xhdpi/splash.png", 1280, 720),
    ("drawable-land-xxhdpi/splash.png", 1600, 960),
    ("drawable-land-xxxhdpi/splash.png", 1920, 1280),
]

for rel_path, w, h in splashes:
    create_splash(w, h, os.path.join(RES_DIR, rel_path))

print("Assets successfully generated!")
