import sys, os
sys.modules['pymupdf.layout'] = None
import fitz
import docx
from docx.shared import Inches, Pt
from docx.enum.text import WD_ALIGN_PARAGRAPH

def is_math_char(c):
    return c in "=+-±×÷∑∫√∂≤≥≠≈→⇒∞παβγδεθλμστφψω\u2212\u2211\u222b\u221a¿_{}^°′″"

def convert(pdf_path, docx_path):
    if not os.path.exists(pdf_path):
        print(f"ERROR: File not found: {pdf_path}", file=sys.stderr)
        return False
        
    doc = fitz.open(pdf_path)
    out_doc = docx.Document()
    
    for s in out_doc.sections:
        s.top_margin = Inches(0.8)
        s.bottom_margin = Inches(0.8)
        s.left_margin = Inches(0.8)
        s.right_margin = Inches(0.8)
        
    for p_idx, page in enumerate(doc):
        if p_idx > 0:
            out_doc.add_page_break()
            
        page_rect = page.rect
        drawings = page.get_drawings()
        blocks = page.get_text("blocks")
        
        formula_boxes = []
        for b in blocks:
            x0, y0, x1, y1, text, b_no, b_type = b
            if b_type != 0: continue
            text_str = text.strip()
            if not text_str: continue
            
            b_rect = fitz.Rect(x0, y0, x1, y1)
            has_sym = any(is_math_char(c) for c in text_str)
            
            nearby_drawings = [d["rect"] for d in drawings if b_rect.intersects(d["rect"]) or (abs(d["rect"].y0 - y0) < 30 and max(x0, d["rect"].x0) <= min(x1, d["rect"].x1) + 30)]
            keywords = ["kuvveti", "yayın", "kuvvet", "force", "sağdaki", "soldaki", "limit", "denklem", "formula"]
            has_kw = any(kw in text_str.lower() for kw in keywords)
            
            if (has_sym and nearby_drawings) or (len(nearby_drawings) > 0 and (len(text_str.split("\n")) > 1 or has_kw)) or (has_sym and len(text_str.split("\n")) > 2):
                box = b_rect
                for dr in nearby_drawings:
                    box = box | dr
                formula_boxes.append(box)
                
        merged_boxes = []
        for box in formula_boxes:
            merged = False
            for i, mb in enumerate(merged_boxes):
                if box.intersects(mb) or (abs(box.y0 - mb.y1) < 30 and max(box.x0, mb.x0) <= min(box.x1, mb.x1) + 40):
                    merged_boxes[i] = mb | box
                    merged = True
                    break
            if not merged:
                merged_boxes.append(box)
                
        final_formula_boxes = []
        for mb in merged_boxes:
            expanded = fitz.Rect(max(0, mb.x0 - 8), max(0, mb.y0 - 6), min(page_rect.width, mb.x1 + 8), min(page_rect.height, mb.y1 + 8))
            final_formula_boxes.append(expanded)
            
        content_items = []
        for fb in final_formula_boxes:
            content_items.append(("formula", fb.y0, fb, None))
            
        for b in blocks:
            x0, y0, x1, y1, text, b_no, b_type = b
            if b_type != 0: continue
            b_rect = fitz.Rect(x0, y0, x1, y1)
            inside_formula = any(b_rect.intersects(fb) or (abs(b_rect.y0 - fb.y0) < 15 and abs(b_rect.x0 - fb.x0) < 100) for fb in final_formula_boxes)
            if not inside_formula:
                clean_lines = [l.strip() for l in text.split("\n") if l.strip()]
                if clean_lines:
                    content_items.append(("text", y0, b_rect, clean_lines))
                    
        content_items = sorted(content_items, key=lambda item: item[1])
        
        for item_type, y, rect, data in content_items:
            if item_type == "formula":
                pix = page.get_pixmap(clip=rect, dpi=300)
                tmp_path = f"tmp_f_{p_idx}_{int(y)}_{os.getpid()}.png"
                pix.save(tmp_path)
                
                p = out_doc.add_paragraph()
                p.alignment = WD_ALIGN_PARAGRAPH.CENTER
                p.paragraph_format.space_before = Pt(8)
                p.paragraph_format.space_after = Pt(8)
                run = p.add_run()
                width_in = min(6.0, max(1.5, pix.width / 300.0))
                run.add_picture(tmp_path, width=Inches(width_in))
                
                if os.path.exists(tmp_path):
                    try: os.remove(tmp_path)
                    except: pass
            elif item_type == "text":
                for line in data:
                    p = out_doc.add_paragraph(line)
                    p.paragraph_format.space_after = Pt(4)
                    
    out_doc.save(docx_path)
    print("SUCCESS")
    return True

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: ld_pdf_math_engine <input.pdf> <output.docx>")
        sys.exit(1)
    convert(sys.argv[1], sys.argv[2])
