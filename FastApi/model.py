import pandas as pd
import joblib
from sklearn.model_selection import train_test_split
# Заменили Tfidf на Hashing
from sklearn.feature_extraction.text import HashingVectorizer
from sklearn.linear_model import SGDClassifier

from sklearn.pipeline import Pipeline
from sklearn.metrics import classification_report, accuracy_score

# 1. Загрузка данных
try:
    df = pd.read_csv("dataset.csv", sep=',')
except:
    df = pd.read_csv("dataset.csv", sep=';')

df = df.dropna(subset=["description", "category"])

# Обработка редких категорий
min_count = 3
counts = df["category"].value_counts()
df.loc[df["category"].isin(counts[counts < min_count].index), "category"] = "Прочие операции"

X = df["description"].astype(str)
y = df["category"].astype(str)

X_train, X_test, y_train, y_test = train_test_split(X, y, test_size=0.2, random_state=42, stratify=y)

# 2. Создание Векторизатора и Модели
vectorizer = HashingVectorizer(n_features=2**18, alternate_sign=False, ngram_range=(1, 2))
clf = SGDClassifier(loss='log_loss', penalty='l2', alpha=1e-4, learning_rate='adaptive', eta0=0.1,  random_state=4)

# 3. Обучение
X_train_vec = vectorizer.transform(X_train)
X_test_vec = vectorizer.transform(X_test)

clf.fit(X_train_vec, y_train)

# 4. Проверка
y_pred = clf.predict(X_test_vec)
accuracy = accuracy_score(y_test, y_pred)

print(f"\nSGD Accuracy (Hashing): {accuracy:.4f} ({accuracy*100:.2f}%)")
print(classification_report(y_test, y_pred, zero_division=0))

# 5. Сохранение
joblib.dump(clf, "sgd_model.pkl")
joblib.dump(vectorizer, "hashing_vect.pkl")

print("Файлы sgd_model.pkl и hashing_vect.pkl сохранены.")
