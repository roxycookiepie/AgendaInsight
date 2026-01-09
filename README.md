# City Agenda AI – Public Demo

This repository demonstrates how **AI can transform unstructured city council agenda documents into structured, actionable project data**.

It showcases a secure, configurable workflow that combines document processing with large language models (LLMs) to identify and summarize **professional engineering projects** from municipal agendas.

---

## What This Demo Shows

- 📄 Automated extraction of text from city agenda PDFs  
- 🧠 AI-based identification of relevant engineering projects  
- 🧾 Structured output (dates, consultants, amounts, categories)  
- 🔐 Security-conscious design suitable for public demonstration  

---

## Why It Matters

City agendas often contain early signals of upcoming infrastructure and transportation projects, but the information is difficult to extract at scale.

This demo illustrates how AI can:
- Reduce manual document review  
- Improve consistency and accuracy  
- Enable faster insight into public-sector opportunities  

---

## Design Principles

- **Public-safe**: No internal URLs, credentials, or tenant data  
- **Config-driven**: Environment details resolved at runtime  
- **Explainable**: Clear rules for what content is included or excluded  
- **Extensible**: Designed to support additional document types and workflows  

---

## What’s Included

- Core agenda-processing pipeline  
- AI prompt logic for structured data extraction  
- API endpoint for triggering document processing  

---

## What’s Not Included

- OCR for scanned PDFs  
- Authentication or authorization  
- Production security controls  
- Client- or tenant-specific logic  

These elements are intentionally excluded to keep the demo lightweight and portable.

---

## Disclaimer

This repository is provided **for demonstration purposes only** and is not intended for production use without additional security, validation, and operational controls.
