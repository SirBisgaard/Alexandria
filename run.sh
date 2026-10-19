#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

mkdir -p App_Data

docker run --rm -p 8080:8080 \
  --user "$(id -u):$(id -g)" \
  -v /path/to/your/ebooks:/books/ebooks \
  -v /path/to/your/audiobooks:/books/audiobooks \
  -v "$(pwd)/App_Data:/data" \
  -e AlexandriaSettings__PublicUrl=http://localhost:8080 \
  -e AlexandriaSettings__DataFolder=/data \
  -e AlexandriaSettings__EbookFolder=/books/ebooks \
  -e AlexandriaSettings__AudiobookFolder=/books/audiobooks \
  alexandria
