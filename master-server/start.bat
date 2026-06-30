@echo off
cd /d %~dp0
pip install -r requirements.txt -q
REM Local dev: allow placeholder secrets so the app starts without a .env.
set REQUIRE_SECURE_SECRETS=false
uvicorn app.main:app --host 0.0.0.0 --port 8000 --reload
