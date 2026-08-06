#!/usr/bin/env bash
# Routes to GPT-5.5 for code and math, GPT-5.4 for creative and general.
#
#   source ./run-gpt5.sh
#
# Source it so the variables stay set in your shell, then `dotnet run` again to change
# the mix without editing anything.

if [ -z "${OPENAI_API_KEY:-}" ]; then
  echo "Set OPENAI_API_KEY first, e.g. export OPENAI_API_KEY=sk-..."
  return 2>/dev/null || exit 1
fi

# Default for any route without its own override, and for anything added later.
export OPENAI_CHAT_MODEL="gpt-5.4"
export OPENAI_EMBEDDING_MODEL="text-embedding-3-small"

# code: the strongest model, thinking hard.
export OPENAI_CHAT_MODEL_CODE="gpt-5.5"
export OPENAI_REASONING_CODE="high"

# math: same model, less thinking, so it stays quicker and cheaper.
export OPENAI_CHAT_MODEL_MATH="gpt-5.5"
export OPENAI_REASONING_MATH="medium"

# creative: cheaper model, turned up for variety.
export OPENAI_CHAT_MODEL_CREATIVE="gpt-5.4"
export OPENAI_TEMPERATURE_CREATIVE="0.8"

# general: the cheap default, left alone.
export OPENAI_CHAT_MODEL_GENERAL="gpt-5.4"

echo "Routes configured:"
echo "  code      gpt-5.5   reasoning high"
echo "  math      gpt-5.5   reasoning medium"
echo "  creative  gpt-5.4   temperature 0.8"
echo "  general   gpt-5.4"
echo
echo "Now run: dotnet run"
