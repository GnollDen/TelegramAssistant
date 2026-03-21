# Data Formats

## `data/raw/messages.json`

```json
[
  {
    "id": "m1",
    "chat_id": "chat-main",
    "sender": "user",
    "timestamp": "2026-03-18T21:00:00",
    "text": "How was your day?"
  },
  {
    "id": "m2",
    "chat_id": "chat-main",
    "sender": "other",
    "timestamp": "2026-03-18T21:15:00",
    "text": "Pretty good, just tired after work"
  }
]
```

## `data/raw/offline_notes.json`

```json
[
  {
    "id": "o1",
    "timestamp": "2026-03-17T20:30:00",
    "title": "Walk after dinner",
    "summary": "Conversation was easy, she laughed a lot, no explicit flirting, goodbye was warm.",
    "sentiment": 0.5
  }
]
```

## Notes

- `sender` is normalized to `user` or `other`
- timestamps should be ISO 8601
- `sentiment` is optional and can be added manually after offline interactions
