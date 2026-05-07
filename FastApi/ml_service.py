import os
from pathlib import Path
from typing import List, Optional

import joblib
import pandas as pd
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from sklearn.feature_extraction.text import HashingVectorizer
from sklearn.linear_model import SGDClassifier

from enrichment_tool import (
    FALLBACK_CATEGORY,
    CategorySuggestion,
    ask_mistral_for_category,
    contains_private_data,
    enrich_locally,
    extract_merchant_name,
    lookup_merchant_rules,
    normalize_description,
    search_web_for_merchant,
)


app = FastAPI(title="Transaction Categorization API")

BASE_DIR = Path(__file__).resolve().parent
DATASET_PATH = BASE_DIR / "dataset.csv"
MODEL_FILE = BASE_DIR / "sgd_model.pkl"
VECT_FILE = BASE_DIR / "hashing_vect.pkl"
LOW_CONFIDENCE_THRESHOLD = 0.55
REVIEW_CATEGORIES = {FALLBACK_CATEGORY, "Неизвестно"}

clf = None
vectorizer = None
exact_matches = {}

ALL_CLASSES = [
    "Супермаркет",
    "Транспорт",
    "Перевод на карту",
    "Перевод с карты",
    "Перевод СБП",
    "Рестораны и кафе",
    "Прочие операции",
    "Кэшбэк",
    "Здоровье и красота",
    "Коммунальные платежи, связь, интернет",
    "Маркетплейс",
    "Отдых и развлечения",
    "Пополнение",
    "Зарплата",
    "Фастфуд",
    "Доставка",
    "Стипендия",
    "Канцтовары",
    "Дом и ремонт",
    "Электроника",
]


class Transaction(BaseModel):
    description: str
    amount: Optional[str] = None
    date: Optional[str] = None
    bank: Optional[str] = None
    type: Optional[str] = None


class PredictRequest(BaseModel):
    transactions: List[Transaction]


class PredictedTransaction(BaseModel):
    description: str
    predicted_category: str
    confidence: float
    source: str
    needs_review: bool
    suggestions: List[CategorySuggestion]
    suggestion_reason: Optional[str] = None


class PredictResponse(BaseModel):
    success: bool
    results: List[PredictedTransaction]


class FeedbackItem(BaseModel):
    description: str
    correct_category: str


class FeedbackRequest(BaseModel):
    items: List[FeedbackItem]


class EnrichWebRequest(BaseModel):
    description: str
    amount: Optional[str] = None
    available_categories: List[str]


class EnrichWebResponse(BaseModel):
    success: bool
    suggestion: Optional[CategorySuggestion] = None
    safe_query: Optional[str] = None
    message: str


def save_model():
    joblib.dump(clf, MODEL_FILE)
    joblib.dump(vectorizer, VECT_FILE)


def load_exact_matches():
    global exact_matches
    exact_matches = {}
    if not DATASET_PATH.exists():
        return

    try:
        df = pd.read_csv(DATASET_PATH, sep=";", usecols=["description", "category"])
        df = df.dropna(subset=["description", "category"])
        df = df.drop_duplicates(subset=["description"], keep="last")
        exact_matches = {
            normalize_description(str(row["description"])): str(row["category"])
            for _, row in df.iterrows()
            if normalize_description(str(row["description"]))
        }
        print(f"Loaded {len(exact_matches)} exact category rules.")
    except Exception as exc:
        print(f"Exact rules loading error: {exc}")


def init_model_from_dataset():
    global clf, vectorizer, ALL_CLASSES

    vectorizer = HashingVectorizer(
        n_features=2**18,
        alternate_sign=False,
        ngram_range=(1, 2),
    )

    clf = SGDClassifier(
        loss="log_loss",
        penalty="l2",
        alpha=1e-4,
        learning_rate="adaptive",
        eta0=0.1,
        random_state=4,
    )

    if not DATASET_PATH.exists():
        print("dataset.csv not found, model will be trained through /feedback")
        return

    try:
        df = pd.read_csv(DATASET_PATH, sep=";")
        df = df.dropna(subset=["description", "category"])

        x = df["description"].astype(str)
        y = df["category"].astype(str)

        ALL_CLASSES = list(set(ALL_CLASSES + y.unique().tolist()))
        x_vec = vectorizer.transform(x)
        clf.partial_fit(x_vec, y, classes=ALL_CLASSES)

        save_model()
        print("Model initialized from dataset.csv")
    except Exception as exc:
        print(f"Initial model training error: {exc}")


def load_model():
    global clf, vectorizer
    if MODEL_FILE.exists() and VECT_FILE.exists():
        clf = joblib.load(MODEL_FILE)
        vectorizer = joblib.load(VECT_FILE)
        print("Model loaded from disk.")
    else:
        print("Model files not found, initializing from scratch.")
        init_model_from_dataset()


@app.on_event("startup")
def startup_event():
    load_model()
    load_exact_matches()


@app.get("/")
def root():
    return {"message": "ML API is running"}


def make_fallback_suggestion() -> CategorySuggestion:
    return CategorySuggestion(
        category=FALLBACK_CATEGORY,
        confidence=0.2,
        source="fallback",
        reason="Недостаточно уверенности для точной категории.",
    )


def make_prediction_response(
    description: str,
    selected: CategorySuggestion,
    suggestions: List[CategorySuggestion],
) -> PredictedTransaction:
    unique_suggestions = []
    seen = set()
    for suggestion in suggestions + [make_fallback_suggestion()]:
        key = (suggestion.category, suggestion.source)
        if key not in seen:
            seen.add(key)
            unique_suggestions.append(suggestion)

    needs_review = (
        selected.confidence < LOW_CONFIDENCE_THRESHOLD
        or selected.category in REVIEW_CATEGORIES
    )

    return PredictedTransaction(
        description=description,
        predicted_category=selected.category,
        confidence=selected.confidence,
        source=selected.source,
        needs_review=needs_review,
        suggestions=unique_suggestions,
        suggestion_reason=unique_suggestions[0].reason if unique_suggestions else None,
    )


def predict_with_ml(description: str) -> CategorySuggestion:
    if clf is None or vectorizer is None or not hasattr(clf, "coef_"):
        return CategorySuggestion(
            category="Неизвестно",
            confidence=0.0,
            source="ml",
            reason="ML-модель еще не обучена.",
        )

    x_vec = vectorizer.transform([description])
    prediction = str(clf.predict(x_vec)[0])

    confidence = 0.0
    if hasattr(clf, "predict_proba"):
        confidence = float(clf.predict_proba(x_vec)[0].max())

    return CategorySuggestion(
        category=prediction,
        confidence=confidence,
        source="ml",
        reason="Категория предсказана ML-моделью.",
    )


@app.post("/predict", response_model=PredictResponse)
def predict(request: PredictRequest):
    if clf is None or vectorizer is None:
        raise HTTPException(status_code=503, detail="ML-модель не готова")

    if not request.transactions:
        return PredictResponse(success=True, results=[])

    results: List[PredictedTransaction] = []

    for transaction in request.transactions:
        description = transaction.description or ""
        normalized = normalize_description(description)
        local_suggestions = enrich_locally(description, transaction.amount)

        exact_category = exact_matches.get(normalized)
        if exact_category:
            exact_suggestion = CategorySuggestion(
                category=exact_category,
                confidence=1.0,
                source="exact_match",
                reason="Описание найдено в подтвержденном локальном датасете.",
            )
            results.append(
                make_prediction_response(
                    description=description,
                    selected=exact_suggestion,
                    suggestions=[exact_suggestion] + local_suggestions,
                )
            )
            continue

        strong_rule = next(
            (suggestion for suggestion in local_suggestions if suggestion.confidence >= 0.9),
            None,
        )
        if strong_rule:
            results.append(
                make_prediction_response(
                    description=description,
                    selected=strong_rule,
                    suggestions=[strong_rule],
                )
            )
            continue

        ml_suggestion = predict_with_ml(description)
        suggestions = [ml_suggestion] + local_suggestions
        results.append(
            make_prediction_response(
                description=description,
                selected=ml_suggestion,
                suggestions=suggestions,
            )
        )

    return PredictResponse(success=True, results=results)


@app.post("/enrich-web", response_model=EnrichWebResponse)
def enrich_web(request: EnrichWebRequest):
    if contains_private_data(request.description):
        return EnrichWebResponse(
            success=False,
            message="Описание содержит персональные данные или похоже на перевод. Интернет-поиск не выполнялся.",
        )

    merchant_name = extract_merchant_name(request.description)
    if not merchant_name:
        return EnrichWebResponse(
            success=False,
            message="Не удалось извлечь безопасное название организации.",
        )

    safe_query = normalize_description(merchant_name)
    if not safe_query or contains_private_data(safe_query):
        return EnrichWebResponse(
            success=False,
            safe_query=safe_query,
            message="Безопасное название организации не прошло проверку приватности.",
        )

    local_suggestion = lookup_merchant_rules(request.description) or lookup_merchant_rules(merchant_name)
    if local_suggestion:
        return EnrichWebResponse(
            success=True,
            suggestion=local_suggestion,
            safe_query=safe_query,
            message="Категория найдена в локальном словаре мерчантов. Интернет-поиск не выполнялся.",
        )

    web_context = search_web_for_merchant(safe_query)
    if not web_context:
        web_context = (
            "No public search summary was found. Classify using only this sanitized merchant "
            f"name from the bank transaction: {safe_query}."
        )

    suggestion = ask_mistral_for_category(
        merchant_name=safe_query,
        web_context=web_context,
        categories=request.available_categories or [],
    )
    if not suggestion:
        return EnrichWebResponse(
            success=False,
            safe_query=safe_query,
            message="Mistral не вернул уверенную категорию.",
        )

    return EnrichWebResponse(
        success=True,
        suggestion=suggestion,
        safe_query=safe_query,
        message="Категория предложена по безопасному названию организации. Модель не дообучалась.",
    )


@app.post("/feedback")
def feedback(request: FeedbackRequest):
    global clf, exact_matches

    if not request.items:
        return {"success": True, "updated_count": 0, "message": "Нет данных"}

    descriptions = [item.description for item in request.items]
    categories = [item.correct_category for item in request.items]

    try:
        df_new = pd.DataFrame(
            {
                "date": [""] * len(descriptions),
                "amount": [""] * len(descriptions),
                "description": descriptions,
                "bank": [""] * len(descriptions),
                "category": categories,
                "type": [""] * len(descriptions),
            }
        )
        column_order = ["date", "amount", "description", "bank", "category", "type"]
        header_needed = not DATASET_PATH.exists()
        df_new.to_csv(
            DATASET_PATH,
            mode="a",
            sep=";",
            header=header_needed,
            index=False,
            columns=column_order,
        )
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"CSV write error: {exc}")

    try:
        x_vec = vectorizer.transform(descriptions)
        if hasattr(clf, "coef_"):
            clf.partial_fit(x_vec, categories)
        else:
            clf.partial_fit(x_vec, categories, classes=ALL_CLASSES)
        save_model()
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Model training error: {exc}")

    for description, category in zip(descriptions, categories):
        normalized = normalize_description(description)
        if normalized:
            exact_matches[normalized] = category

    return {
        "success": True,
        "updated_count": len(descriptions),
        "message": "Данные сохранены, модель дообучена через подтвержденный /feedback",
    }


if __name__ == "__main__":
    import uvicorn

    print("Starting FastAPI...")
    uvicorn.run(app, host="127.0.0.1", port=8000)
