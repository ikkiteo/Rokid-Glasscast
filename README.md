<p align="center">
  <a href="README.en.md">
    <img alt="English" src="https://img.shields.io/badge/English-238636?style=for-the-badge&logo=googletranslate&logoColor=white">
  </a>
</p>

<h1 align="center">GlassCast</h1>

<p align="center">
  Экран телефона или компьютера на очках Rokid.<br>
  Одна Wi‑Fi сеть, Motion JPEG, чёрные поля на панели не светятся.
</p>

<p align="center">
  <a href="https://github.com/ikkiteo/Rokid-Glasscast/releases/tag/v2.1.0"><img alt="v2.1.0" src="https://img.shields.io/badge/release-v2.1.0-1f6feb?style=flat-square"></a>
  <a href="PROTOCOL.md"><img alt="protocol" src="https://img.shields.io/badge/protocol-GCS1-6e7681?style=flat-square"></a>
</p>

Русская страница. Английский текст и английские сборки — по зелёной кнопке сверху.

## Скачать

Обе версии — одни и те же программы. Отличается только язык интерфейса. На очки и телефон ставится один APK: второй язык заменяет первый.

| | Русский | English |
|---|---|---|
| Очки | [GlassCast-glasses-ru.apk](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-glasses-ru.apk) | [GlassCast-glasses-en.apk](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-glasses-en.apk) |
| Телефон | [GlassCast-phone-ru.apk](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-phone-ru.apk) | [GlassCast-phone-en.apk](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-phone-en.apk) |
| Компьютер | [GlassCast-PC-ru.zip](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-PC-ru.zip) | [GlassCast-PC-en.zip](https://github.com/ikkiteo/Rokid-Glasscast/releases/download/v2.1.0/GlassCast-PC-en.zip) |

Архив для ПК уже собран: `GlassCast.exe`, `VddHost.exe` и папка драйвера. Распакуй и запускай exe из этой папки. .NET Framework 4.8, отдельная установка рантайма не нужна.

Драйвер виртуального монитора — **USB Mobile Monitor** компании Amyuni Technologies. Он не вшит в exe. Лицензия лежит рядом: `driver/usbmmidd/usbmmidd_v2/License.txt`.

## Как это устроено

Очки слушают сеть. Телефон и компьютер только подключаются.

```text
телефон ──JPEG──┐
                ├── TCP 47810 ── очки Rokid
компьютер ──────┘
         поиск по UDP 47811
```

| программа | куда | что делает |
|---|---|---|
| `receiver` | очки | принимает JPEG и рисует его по центру панели |
| `sender` | телефон | снимает экран и шлёт JPEG |
| `windows` | ПК | снимает монитор, окно, зону или виртуальный дисплей |

Протокол описан в [PROTOCOL.md](PROTOCOL.md).

## Быстрый старт

1. Поставь приложение очков и открой его. На панели будет IP.
2. Телефон или ПК должен быть в той же Wi‑Fi сети.
3. На телефоне: **GlassCast Phone** → найти очки → разрешить захват экрана.
4. На ПК: распакуй архив → `GlassCast.exe` → найти очки → **транслировать**.

На очках одно нажатие поворачивает картинку на 90°. Двойное просит новый кадр. Громкость меняет качество потока.

## Компьютер

**Монитор, зона, окно.** В очки уходит выбранный кусок рабочего стола. Захват идёт через DXGI, чтобы курсор на основном экране не мигал.

**Виртуальный дисплей.** Кнопка «драйвер дисплея» один раз ставит драйвер Amyuni. Монитор 1920×1080 включается только на время трансляции этого источника и гаснет, когда трансляция останавливается. Первый запуск спросит права администратора — дальше служба включает экран сама. Перетащи на него окна и транслируй.

**Холст.** Запасное окно без драйвера. F11 разворачивает его на текущий монитор. Это не отдельный дисплей Windows.

Поля над и под картинкой чёрные: на очках эти пиксели не светятся.

## Сборка

```bat
gradlew.bat :receiver:assembleDebug :sender:assembleDebug
windows\build.bat
```

Android-сборки: flavor `ru` и `en`. Готовые APK лежат в `receiver/build/outputs/apk/` и `sender/build/outputs/apk/`.

Windows: `windows\out\ru` и `windows\out\en`. В каждой папке `GlassCast.exe` и `VddHost.exe`. Английская собирается с `/define:EN`.
