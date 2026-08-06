# Routes to GPT-5.5 for code and math, GPT-5.4 for creative and general.
#
#   . .\run-gpt5.ps1
#
# Dot-source it so the variables stay set in your session, then `dotnet run` again to
# change the mix without editing anything.

if (-not $env:OPENAI_API_KEY) {
    Write-Host "Set OPENAI_API_KEY first, e.g. `$env:OPENAI_API_KEY = 'sk-...'" -ForegroundColor Yellow
    return
}

# Default for any route without its own override, and for anything added later.
$env:OPENAI_CHAT_MODEL      = "gpt-5.4"
$env:OPENAI_EMBEDDING_MODEL = "text-embedding-3-small"

# code: the strongest model, thinking hard.
$env:OPENAI_CHAT_MODEL_CODE = "gpt-5.5"
$env:OPENAI_REASONING_CODE  = "high"

# math: same model, less thinking, so it stays quicker and cheaper.
$env:OPENAI_CHAT_MODEL_MATH = "gpt-5.5"
$env:OPENAI_REASONING_MATH  = "medium"

# creative: cheaper model, turned up for variety.
$env:OPENAI_CHAT_MODEL_CREATIVE = "gpt-5.4"
$env:OPENAI_TEMPERATURE_CREATIVE = "0.8"

# general: the cheap default, left alone.
$env:OPENAI_CHAT_MODEL_GENERAL = "gpt-5.4"

Write-Host "Routes configured:" -ForegroundColor Green
Write-Host "  code      gpt-5.5   reasoning high"
Write-Host "  math      gpt-5.5   reasoning medium"
Write-Host "  creative  gpt-5.4   temperature 0.8"
Write-Host "  general   gpt-5.4"
Write-Host ""
Write-Host "Now run: dotnet run"
