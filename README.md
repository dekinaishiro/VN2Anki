# VN2Anki - Quickstart

## About

VN2Anki is a simple and lightweight tool designed to streamline the process of making Anki cards from Visual Novels. It runs in the background and is meant to be used alongside a texthooking software like Textractor or Luna Translator.

## Requirements

1. Textractor or Luna Translator *(configured to copy text to the clipboard)*
2. Anki with the **AnkiConnect** add-on installed

## Setup

* Download the latest release (`.zip` file) from the Releases page.
* Extract it to a folder of your choice and run the `.exe` file.

## How to Use

1. Open the **Settings** (⚙️).
2. Set the **Audio Source** and **Video Source** (your VN window) you want to record.
3. Set up your Anki settings: select the **Deck**, **Note Type**, and the specific **Audio** and **Image fields**.
4. Start the buffer by clicking the **OFF / ON** button.
5. Mine your words normally (e.g., using Yomitan to create a card in Anki).
6. Click **ADD** (Quick Add) on VN2Anki to automatically update that last created card with the current sentence's screenshot and audio.

**Controls:**

* Click **LIST** to open the history window and view/manage your recent captured slots.
* Click **RES** to end and restart the current Session (this clears the history and resets the characters-read stats).
* Click the **⏱** to toggle the mini-stats panel visibility.

https://github.com/user-attachments/assets/d2f8f085-0c69-4e2e-821e-8662a7a81102

## Settings Overview

* **Slot Limit:** The maximum number of sentences (and their media) kept in your RAM. Older slots are automatically deleted. (Default is 30 but you are probably fine way more)
* **Dynamic Timeout:** Calculates the audio recording duration automatically based on the length of the sentence and punctuation pauses.
* **Timeout (Settings):** *(Not recommended)* If Dynamic Timeout is disabled, sets a fixed recording time for every audio clip.
* **Timeout (Anki):** The maximum time (in seconds) the program will wait for AnkiConnect to respond. Increase this if you have a huge Anki database that takes a while to save.

## Troubleshooting
- Downloading NET 8.0 Desktop Runtime might be necessary (most likely won't)
