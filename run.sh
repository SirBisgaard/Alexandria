#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

mkdir -p App_Data

docker run --rm -p 8080:8080 \
  -v /path/to/books/Epubs:/data/ebooks \
  -v /path/to/books/Audiobooks:/data/audiobooks \
  -v "$(pwd)/App_Data:/data/app" \
  -e AlexandriaSettings__EbookFolder=/data/ebooks \
  -e AlexandriaSettings__AudiobookFolder=/data/audiobooks \
  -e AlexandriaSettings__DataFolder=/data/app \
  alexandria
