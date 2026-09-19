"""Generate a small vector TrueType dimension fixture for AutoCAD runtime checks."""

from pathlib import Path

from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas


def main() -> None:
    target = Path(__file__).resolve().parents[1] / "artifacts" / "runtime-dimension-control.pdf"
    target.parent.mkdir(parents=True, exist_ok=True)
    pdfmetrics.registerFont(TTFont("ArialControl", r"C:\Windows\Fonts\arial.ttf"))
    c = canvas.Canvas(str(target), pagesize=(240, 150), pageCompression=0)
    c.setTitle("TeyPdfCad 5200 dimension control")
    c.setLineWidth(0.35)

    # Approximate paper geometry after PDFIMPORT: 52 mm span, 12 mm offsets.
    mm = 72 / 25.4
    x0, y0 = 35.0, 90.0
    x1 = x0 + 52 * mm
    c.line(x0, y0, x1, y0)
    c.line(x0, y0 - 12 * mm, x0, y0 + 1 * mm)
    c.line(x1, y0 - 12 * mm, x1, y0 + 1 * mm)
    c.line(x0 - mm, y0 - mm, x0 + mm, y0 + mm)
    c.line(x1 - mm, y0 - mm, x1 + mm, y0 + mm)
    c.setFont("ArialControl", 7)
    c.drawCentredString((x0 + x1) / 2, y0 + 3 * mm, "5200")
    c.showPage()
    c.save()
    print(target)


if __name__ == "__main__":
    main()
