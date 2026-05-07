import json
import os
import re
from html import unescape
from pathlib import Path
from typing import List, Optional
from urllib.parse import quote_plus

from pydantic import BaseModel

try:
    import requests
except ImportError:
    requests = None


class CategorySuggestion(BaseModel):
    category: str
    confidence: float
    source: str
    reason: str


BASE_DIR = Path(__file__).resolve().parent
DEFAULT_MISTRAL_API_KEY = "WdNK0AwaJg27oRiLv67gd9Ztao8jcAt6"
MERCHANT_RULES_PATH = BASE_DIR / "merchant_rules.json"
TRANSFER_CATEGORY_IN = "Перевод на карту"
TRANSFER_CATEGORY_OUT = "Перевод с карты"
TRANSFER_CATEGORY_SBP = "Перевод СБП"
FALLBACK_CATEGORY = "Прочие операции"

PRIVATE_PATTERNS = [
    r"\+?\d[\d\-\s\(\)]{8,}\d",
    r"\b\d{16}\b",
    r"\b\d{12,20}\b",
    r"\b[А-ЯЁ]\.\s?[А-ЯЁ][А-ЯЁа-яё\-]+\s+[А-ЯЁ][А-ЯЁа-яё\-]+\b",
    r"\b[А-ЯЁ][а-яё]+\s+[А-ЯЁ][а-яё]+\s+[А-ЯЁ][а-яё]+\b",
    r"\bномер\s+сч[её]та\b",
    r"\bсч[её]т\s*\d+",
]

TRANSFER_PATTERNS = [
    r"\bSBOL\b",
    r"\bСБП\b",
    r"\bперевод\s+по\s+номеру\s+телефона\b",
    r"\bперевод\s+на\s+карту\b",
    r"\bперевод\s+с\s+карты\b",
    r"\bвнешний\s+перевод\b",
]

MERCHANT_STOP_WORDS = {
    "OPERATION", "OPERACIYA", "ОПЕРАЦИЯ", "КАРТЕ", "КАРТА", "ПО", "RUS",
    "RU", "MOSCOW", "SPB", "SANKT", "PETERBURG", "VEL", "NOVGOROD",
}


MERCHANT_STOP_WORDS.update({
    "OPLATA", "OOO", "OAO", "IP", "LLC", "LTD", "MOSCOWRU", "MOSCOWRUS",
    "MOSKVA", "MOSKVARUS", "PAYMENT", "SERVICES", "SERVICE", "PROVIDER",
    "CARD", "KARTE", "POKARTE",
})

# Паттерн суффикса Сбербанка: "Операция по карте ****4043" всегда в конце строки
_SBER_CARD_SUFFIX_RE = re.compile(
    r"\.?\s*Операция\s+по\s+карте\s+\*+\d{0,6}\s*$",
    flags=re.IGNORECASE,
)


def strip_sber_suffix(text: str) -> str:
    """Вырезает суффикс 'Операция по карте ****XXXX' из конца строки."""
    return _SBER_CARD_SUFFIX_RE.sub("", text or "").strip()


def get_mistral_api_key() -> Optional[str]:
    for env_name in ("MISTRAL_API_KEY", "MISTRAL_APIKEY"):
        value = os.getenv(env_name)
        if value and value.strip():
            return value.strip()

    return DEFAULT_MISTRAL_API_KEY


def normalize_description(text: str) -> str:
    # Сначала убираем сберовский суффикс из оригинала (до upper)
    value = strip_sber_suffix(text or "")
    value = value.upper().replace("Ё", "Е")
    value = re.sub(r"\*{2,}\d*", " ", value)
    value = re.sub(r"\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\b", " ", value)
    value = re.sub(r"\bОПЕРАЦИЯ\s+ПО\s+КАРТЕ\b.*$", " ", value)
    value = re.sub(r"[^\wА-ЯA-Z\s\-]+", " ", value, flags=re.IGNORECASE)
    value = re.sub(r"\s+", " ", value).strip()
    return value


def clean_merchant_name(description: str) -> Optional[str]:
    normalized = normalize_description(description)
    if not normalized:
        return None

    cleanup_patterns = [
        r"\bОПЛАТА\s+В\b",
        r"\bОПЛАТА\b",
        r"\bOPLATA\s+V\b",
        r"\bOPLATA\b",
        r"\bYM\b",
        r"\bОПЕРАЦИЯ\s+ПО\s*КАРТЕ\b.*$",
        r"\bОПЕРАЦИЯ\s+ПОКАРТЕ\b.*$",
        r"\bPO\s*KARTE\b.*$",
        r"\bPOKARTE\b.*$",
        r"\bOOO\b",
        r"\bООО\b",
        r"\bOAO\b",
        r"\bИП\b",
        r"\bIP\b",
    ]

    cleaned = normalized
    for pattern in cleanup_patterns:
        cleaned = re.sub(pattern, " ", cleaned, flags=re.IGNORECASE)

    cleaned = re.sub(r"\bMOSCOW\s*RUS?\b", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"\bMOSCOWRU(S)?\b", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"MOSCOWRU(S)?$", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"\bMOSKVA\s*RUS?\b", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"\bMOSKVARU(S)?\b", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"MOSKVA.*$", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"\bVEL\s+NOVGOROD\b", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"\bNOVGOROD\b", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"\bRUS\b|\bRU\b", " ", cleaned, flags=re.IGNORECASE)
    cleaned = re.sub(r"\b\d+\b", " ", cleaned)

    tokens = [
        token for token in cleaned.split()
        if len(token) >= 2 and token not in MERCHANT_STOP_WORDS
    ]
    if not tokens:
        return None

    return " ".join(tokens[:4]).strip() or None


def contains_private_data(text: str) -> bool:
    # Убираем сберовский суффикс ПЕРЕД проверкой приватных паттернов
    text_clean = strip_sber_suffix(text)

    normalized = normalize_description(text_clean)
    normalized = re.sub(r"\*{2,}\d{2,4}", " ", normalized)
    normalized = re.sub(r"\bОПЕРАЦИЯ\s+ПО\s+КАРТЕ\b", " ", normalized)
    normalized = re.sub(r"\s+", " ", normalized).strip()

    if any(re.search(pattern, text_clean, re.IGNORECASE) for pattern in PRIVATE_PATTERNS):
        return True
    return any(re.search(pattern, normalized, re.IGNORECASE) for pattern in TRANSFER_PATTERNS)


def _load_merchant_rules() -> dict:
    if not MERCHANT_RULES_PATH.exists():
        return {}

    try:
        with MERCHANT_RULES_PATH.open("r", encoding="utf-8") as file:
            return json.load(file)
    except Exception:
        return {}


def extract_merchant_name(description: str) -> Optional[str]:
    if contains_private_data(description):
        return None

    raw_upper = (description or "").upper()
    if re.search(r"\bAIA\W*5\s*KA\b", raw_upper, re.IGNORECASE):
        return "AIA*5KA"
    if re.search(r"\bYM\W*FAST\W*ANIME", raw_upper, re.IGNORECASE):
        return "FAST ANIME"

    normalized = normalize_description(description)
    if not normalized:
        return None

    rules = _load_merchant_rules()
    for merchant in sorted(rules.keys(), key=len, reverse=True):
        merchant_norm = normalize_description(merchant)
        if merchant_norm and re.search(rf"(^|\s){re.escape(merchant_norm)}(\s|$)", normalized):
            return merchant

    cleaned_merchant = clean_merchant_name(description)
    if cleaned_merchant:
        return cleaned_merchant

    tokens = [
        token for token in normalized.split()
        if len(token) >= 3 and not token.isdigit() and token not in MERCHANT_STOP_WORDS
    ]
    if not tokens:
        return None

    return " ".join(tokens[:3]).strip() or None


def lookup_merchant_rules(description: str) -> Optional[CategorySuggestion]:
    normalized = normalize_description(description)
    rules = _load_merchant_rules()

    for merchant, category in sorted(rules.items(), key=lambda item: len(item[0]), reverse=True):
        merchant_norm = normalize_description(merchant)
        if merchant_norm and re.search(rf"(^|\s){re.escape(merchant_norm)}(\s|$)", normalized):
            return CategorySuggestion(
                category=category,
                confidence=0.95,
                source="merchant_rule",
                reason=f"Описание содержит мерчанта из локального словаря: {merchant}",
            )

    return None


def suggest_by_transfer_rules(description: str, amount: Optional[str]) -> Optional[CategorySuggestion]:
    normalized = normalize_description(description)
    amount_value = (amount or "").strip()

    if re.search(r"\bСБП\b|перевод\s+по\s+номеру\s+телефона", normalized, re.IGNORECASE):
        return CategorySuggestion(
            category=TRANSFER_CATEGORY_SBP,
            confidence=0.95,
            source="transfer_rule",
            reason="Описание похоже на перевод через СБП.",
        )

    if re.search(r"\bперевод\s+на\s+карту\b", normalized, re.IGNORECASE):
        return CategorySuggestion(
            category=TRANSFER_CATEGORY_OUT,
            confidence=0.95,
            source="transfer_rule",
            reason="Описание содержит перевод на карту.",
        )

    if re.search(r"\bперевод\s+с\s+карты\b", normalized, re.IGNORECASE):
        return CategorySuggestion(
            category=TRANSFER_CATEGORY_IN,
            confidence=0.95,
            source="transfer_rule",
            reason="Описание содержит перевод с карты.",
        )

    if re.search(r"\bSBOL\b", normalized, re.IGNORECASE):
        category = TRANSFER_CATEGORY_IN if amount_value.startswith("+") else TRANSFER_CATEGORY_OUT
        return CategorySuggestion(
            category=category,
            confidence=0.9,
            source="transfer_rule",
            reason="Описание содержит SBOL-перевод; направление определено по сумме.",
        )

    return None


def enrich_locally(description: str, amount: Optional[str]) -> List[CategorySuggestion]:
    suggestions: List[CategorySuggestion] = []

    transfer_suggestion = suggest_by_transfer_rules(description, amount)
    if transfer_suggestion:
        suggestions.append(transfer_suggestion)

    merchant_suggestion = lookup_merchant_rules(description)
    if merchant_suggestion:
        suggestions.append(merchant_suggestion)

    return suggestions


def search_web_for_merchant(merchant_name: str) -> Optional[str]:
    if requests is None:
        return None

    safe_query = normalize_description(merchant_name)
    if not safe_query or contains_private_data(safe_query):
        return None

    query_candidates = [
        f'"{safe_query}"',
        f'"{safe_query}" company',
        f'"{safe_query}" merchant',
        f"{safe_query} company",
        f"{safe_query} merchant",
        f"{safe_query} official",
        f"{safe_query} store",
    ]
    bing_context = _search_bing_html(query_candidates)
    if bing_context:
        return bing_context

    html_context = _search_duckduckgo_html([
        safe_query,
        f"{safe_query} company",
        f"{safe_query} merchant",
        f"{safe_query} official",
        f"{safe_query} store",
    ])
    if html_context:
        return html_context

    parts = []
    query_candidates = [
        safe_query,
        f"{safe_query} company",
        f"{safe_query} merchant",
        f"{safe_query} магазин",
        f"{safe_query} организация",
    ]

    for query in query_candidates:
        url = (
            "https://api.duckduckgo.com/"
            f"?q={quote_plus(query)}&format=json&no_redirect=1&no_html=1&skip_disambig=1"
        )

        try:
            response = requests.get(url, timeout=8, headers={"User-Agent": "FinancialAccounting/1.0"})
            response.raise_for_status()
            data = response.json()
        except Exception:
            continue

        for key in ("AbstractText", "Heading"):
            value = data.get(key)
            if value:
                parts.append(str(value))

        for item in data.get("RelatedTopics", [])[:5]:
            if isinstance(item, dict) and item.get("Text"):
                parts.append(str(item["Text"]))

        if parts:
            break

    context = " ".join(parts)
    context = re.sub(r"\s+", " ", context).strip()
    if context:
        return context[:2000]

    return (
        "No public search summary was found. Classify using only this sanitized merchant "
        f"name from the bank transaction: {safe_query}."
    )


def _search_duckduckgo_html(query_candidates: List[str]) -> Optional[str]:
    snippets = []

    for query in query_candidates:
        try:
            response = requests.post(
                "https://html.duckduckgo.com/html/",
                data={"q": query},
                timeout=10,
                headers={
                    "User-Agent": "Mozilla/5.0 FinancialAccounting/1.0",
                    "Content-Type": "application/x-www-form-urlencoded",
                },
            )
            response.raise_for_status()
        except Exception:
            continue

        titles = re.findall(
            r'class="result__a"[^>]*>(.*?)</a>',
            response.text,
            flags=re.IGNORECASE | re.DOTALL,
        )
        body_snippets = re.findall(
            r'class="result__snippet"[^>]*>(.*?)</(?:a|div)>',
            response.text,
            flags=re.IGNORECASE | re.DOTALL,
        )

        for value in titles[:5] + body_snippets[:5]:
            cleaned = _clean_html_fragment(value)
            if cleaned and cleaned not in snippets:
                snippets.append(cleaned)

        if snippets:
            break

    context = " ".join(snippets)
    context = re.sub(r"\s+", " ", context).strip()
    return context[:2000] if context else None


def _search_bing_html(query_candidates: List[str]) -> Optional[str]:
    snippets = []

    for query in query_candidates:
        try:
            response = requests.get(
                "https://www.bing.com/search",
                params={"q": query, "setlang": "ru-RU"},
                timeout=10,
                headers={"User-Agent": "Mozilla/5.0 FinancialAccounting/1.0"},
            )
            response.raise_for_status()
        except Exception:
            continue

        result_blocks = re.findall(
            r'<li class="b_algo"[^>]*>(.*?)</li>',
            response.text,
            flags=re.IGNORECASE | re.DOTALL,
        )

        for block in result_blocks[:5]:
            title_match = re.search(r"<h2[^>]*>(.*?)</h2>", block, flags=re.IGNORECASE | re.DOTALL)
            snippet_match = re.search(r"<p[^>]*>(.*?)</p>", block, flags=re.IGNORECASE | re.DOTALL)

            for match in (title_match, snippet_match):
                if not match:
                    continue

                cleaned = _clean_html_fragment(match.group(1))
                if cleaned and cleaned not in snippets:
                    snippets.append(cleaned)

        if snippets:
            break

    context = " ".join(snippets)
    context = re.sub(r"\s+", " ", context).strip()
    return context[:2000] if context else None


def _clean_html_fragment(value: str) -> str:
    cleaned = re.sub(r"<[^>]+>", " ", value or "")
    cleaned = unescape(cleaned)
    cleaned = re.sub(r"\s+", " ", cleaned)
    return cleaned.strip()


def ask_mistral_for_category(
    merchant_name: str,
    web_context: str,
    categories: List[str],
) -> Optional[CategorySuggestion]:
    if requests is None:
        return None

    api_key = get_mistral_api_key()
    if not api_key:
        return None

    allowed_categories = [c for c in categories if c and c.strip()]
    if FALLBACK_CATEGORY not in allowed_categories:
        allowed_categories.append(FALLBACK_CATEGORY)

    payload = {
        "model": os.getenv("MISTRAL_MODEL", "mistral-small-latest"),
        "temperature": 0,
        "max_tokens": 300,
        "response_format": {"type": "json_object"},
        "messages": [
            {
                "role": "system",
                "content": (
                    "Ты классифицируешь банковскую операцию по информации об организации. "
                    "Выбери одну категорию только из списка. Не придумывай новую категорию. "
                    "Если информации недостаточно, выбери 'Прочие операции'. Верни только JSON."
                ),
            },
            {
                "role": "user",
                "content": json.dumps(
                    {
                        "merchant_name": merchant_name,
                        "web_context": web_context,
                        "available_categories": allowed_categories,
                        "response_schema": {
                            "category": "string",
                            "confidence": 0.0,
                            "reason": "string",
                        },
                    },
                    ensure_ascii=False,
                ),
            },
        ],
    }

    try:
        response = requests.post(
            "https://api.mistral.ai/v1/chat/completions",
            headers={"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"},
            json=payload,
            timeout=20,
        )
        response.raise_for_status()
        data = response.json()
        content = data["choices"][0]["message"]["content"]
        result = json.loads(content)
    except Exception:
        return None

    category = str(result.get("category", FALLBACK_CATEGORY)).strip()
    if category not in allowed_categories:
        category = FALLBACK_CATEGORY

    try:
        confidence = float(result.get("confidence", 0.0))
    except (TypeError, ValueError):
        confidence = 0.0

    confidence = max(0.0, min(1.0, confidence))
    if confidence < 0.45:
        category = FALLBACK_CATEGORY

    return CategorySuggestion(
        category=category,
        confidence=confidence,
        source="web_ai",
        reason=str(result.get("reason") or "Категория выбрана по описанию организации из внешнего поиска."),
    )


def enrich_with_web_ai(description: str, categories: List[str]) -> Optional[CategorySuggestion]:
    if contains_private_data(description):
        return None

    merchant_name = extract_merchant_name(description)
    if not merchant_name:
        return None

    web_context = search_web_for_merchant(merchant_name)
    if not web_context:
        return None

    return ask_mistral_for_category(merchant_name, web_context, categories)
