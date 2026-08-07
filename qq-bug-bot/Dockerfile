FROM python:3.12-slim-bookworm

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates gh \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY bot.py storage.py github_issue.py export_by_date.py mark.py dedup.py ./

RUN groupadd --gid 10001 bugbot \
    && useradd --create-home --uid 10001 --gid bugbot bugbot \
    && mkdir -p /data/reports \
    && chown -R bugbot:bugbot /app /data

USER bugbot

CMD ["python", "bot.py"]
