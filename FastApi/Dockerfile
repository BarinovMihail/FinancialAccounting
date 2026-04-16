# Берем легкий Python 3.10
FROM python:3.10-slim

# Делаем папку /app рабочей
WORKDIR /app

# Сначала копируем только файл зависимостей (для кэширования слоев Docker)
COPY requirements.txt .

# Устанавливаем библиотеки
RUN pip install --no-cache-dir -r requirements.txt

# Копируем сам код приложения
COPY . .

# Говорим Докеру, что сервис слушает порт 8000
EXPOSE 8000

# Команда запуска
CMD ["uvicorn", "ml_service:app", "--host", "0.0.0.0", "--port", "8000"]
