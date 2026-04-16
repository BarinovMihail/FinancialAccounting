from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List
import joblib
import pandas as pd
import os

from sklearn.linear_model import SGDClassifier
from sklearn.feature_extraction.text import HashingVectorizer
from sklearn.metrics import accuracy_score, classification_report

app = FastAPI(title="Transaction Categorization API")

DATASET_PATH = "dataset.csv"
MODEL_FILE = "sgd_model.pkl"
VECT_FILE = "hashing_vect.pkl"

clf = None
vectorizer = None
exact_matches = {}
ALL_CLASSES = [
    "Супермаркет", "Транспорт", "Перевод на карту", "Перевод с карты",
    "Перевод СБП", "Рестораны и кафе", "Прочие операции", "Кэшбэк", "Здоровье и красота",
    "Коммунальные платежи, связь, интернет", "Маркетплейс", "Отдых и развлечения",
    "Пополнение", "Зарплата", "Фастфуд", "Доставка", "Стипендия", "Канцтовары", "Дом и ремонт"

]

# -------- Pydantic модели --------

class Transaction(BaseModel):
    description: str

class PredictRequest(BaseModel):
    transactions: List[Transaction]

class PredictedTransaction(BaseModel):
    description: str
    predicted_category: str
    confidence: float

class PredictResponse(BaseModel):
    success: bool
    results: List[PredictedTransaction]

class FeedbackItem(BaseModel):
    description: str
    correct_category: str

class FeedbackRequest(BaseModel):
    items: List[FeedbackItem]


# -------- Инициализация --------

def save_model():
    joblib.dump(clf, MODEL_FILE)
    joblib.dump(vectorizer, VECT_FILE)

def load_exact_matches():
    """Загружаем последние категории для описаний из CSV в словарь."""
    global exact_matches
    exact_matches = {}
    if not os.path.exists(DATASET_PATH):
        return
    try:
        df = pd.read_csv(DATASET_PATH, sep=';', usecols=['description', 'category'])
        df = df.dropna(subset=['description', 'category'])
        df = df.drop_duplicates(subset=['description'], keep='last')
        exact_matches = dict(zip(df['description'], df['category']))
        print(f"Загружено {len(exact_matches)} точных правил.")
    except Exception as e:
        print(f"Ошибка загрузки точных правил: {e}")

def init_model_from_dataset():
    """Первичное обучение модели на всём dataset.csv (если он есть)."""
    global clf, vectorizer, ALL_CLASSES

    vectorizer = HashingVectorizer(
        n_features=2**18,
        alternate_sign=False,
        ngram_range=(1, 2)
    )

    clf = SGDClassifier(
        loss='log_loss',
        penalty='l2',
        alpha=1e-4,
        learning_rate='adaptive',
        eta0=0.1,
        random_state=4
    )

    if not os.path.exists(DATASET_PATH):
        print("dataset.csv не найден, модель будет обучаться только через /feedback")
        return

    try:
        df = pd.read_csv(DATASET_PATH, sep=';')
        df = df.dropna(subset=['description', 'category'])

        X = df['description'].astype(str)
        y = df['category'].astype(str)

        ALL_CLASSES = list(set(ALL_CLASSES + y.unique().tolist()))

        X_vec = vectorizer.transform(X)
        clf.partial_fit(X_vec, y, classes=ALL_CLASSES)

        save_model()
        print("Модель первично обучена на dataset.csv")
    except Exception as e:
        print(f"Ошибка первичного обучения: {e}")

def load_model():
    global clf, vectorizer
    if os.path.exists(MODEL_FILE) and os.path.exists(VECT_FILE):
        clf = joblib.load(MODEL_FILE)
        vectorizer = joblib.load(VECT_FILE)
        print("Модель загружена с диска.")
    else:
        print("Файлы модели не найдены, инициализируем с нуля.")
        init_model_from_dataset()

@app.on_event("startup")
def startup_event():
    load_model()
    load_exact_matches()


# -------- Эндпоинты --------

@app.get("/")
def root():
    return {"message": "ML API is running"}


@app.post("/predict", response_model=PredictResponse)
def predict(request: PredictRequest):
    if clf is None or vectorizer is None:
        raise HTTPException(status_code=503, detail="Модель не готова")

    if not request.transactions:
        return PredictResponse(success=True, results=[])

    texts = [t.description for t in request.transactions]

    temp_results: List[PredictedTransaction] = [None] * len(texts)
    indices_to_predict = []
    texts_to_predict = []

    for i, desc in enumerate(texts):
        if desc in exact_matches:
            temp_results[i] = PredictedTransaction(
                description=desc,
                predicted_category=exact_matches[desc],
                confidence=1.0
            )
        else:
            indices_to_predict.append(i)
            texts_to_predict.append(desc)

    if texts_to_predict:
        X_vec = vectorizer.transform(texts_to_predict)

        if not hasattr(clf, "coef_"):
            for j, idx in enumerate(indices_to_predict):
                temp_results[idx] = PredictedTransaction(
                    description=texts_to_predict[j],
                    predicted_category="Неизвестно",
                    confidence=0.0
                )
        else:
            preds = clf.predict(X_vec)
            probs = clf.predict_proba(X_vec)

            for j, idx in enumerate(indices_to_predict):
                temp_results[idx] = PredictedTransaction(
                    description=texts_to_predict[j],
                    predicted_category=str(preds[j]),
                    confidence=float(probs[j].max())
                )

    return PredictResponse(success=True, results=temp_results)


@app.post("/feedback")
def feedback(request: FeedbackRequest):
    """
    Дообучение модели и обновление правил по новым примерам.
    """
    global clf, exact_matches

    if not request.items:
        return {"success": True, "updated_count": 0, "message": "Нет данных"}

    descriptions = [i.description for i in request.items]
    categories = [i.correct_category for i in request.items]

    try:
        df_new = pd.DataFrame({
            'date': [''] * len(descriptions),
            'amount': [''] * len(descriptions),
            'description': descriptions,
            'bank': [''] * len(descriptions),
            'category': categories,
            'type': [''] * len(descriptions)
        })
        column_order = ['date', 'amount', 'description', 'bank', 'category', 'type']
        header_needed = not os.path.exists(DATASET_PATH)
        df_new.to_csv(
            DATASET_PATH,
            mode='a',
            sep=';',
            header=header_needed,
            index=False,
            columns=column_order
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Ошибка записи в CSV: {e}")

    try:
        X_vec = vectorizer.transform(descriptions)
        clf.partial_fit(X_vec, categories)
        save_model()
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Ошибка дообучения: {e}")

    for d, c in zip(descriptions, categories):
        exact_matches[d] = c

    return {
        "success": True,
        "updated_count": len(descriptions),
        "message": "Данные сохранены и модель дообучена"
    }

if __name__ == "__main__":
    import uvicorn
    print("Запуск FastAPI...")
    uvicorn.run(app, host="127.0.0.1", port=8000)
