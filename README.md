<img src="https://img.shields.io/badge/VibeCoded-100%25-green" alt="AI Generated Content"/>

# Fontastic!

Unity editor tool to preview fonts easily!

### Installation
- Copy the editor script to your project (into Assets/Editor/ folder) from https://github.com/unitycoder/Fontastic/tree/main/Assets/Editor/UnityLibrary/Fontastic

### Usage
- Open the tool from menu: Tools/UnityLibrary/Fontastic
- Select from text item in UI
- Click fetch fonts (defaults to getting locally installed fonts, or you can pick the web fonts radio button)
- Click font name to preview it on your selected UI object
- If its good font, you can press "Copy current font to Assets/Fonts" so it stays in your project
- If you want to restore original font for your selected object, press "Restore original fonts"

### Why?
Without this plugin, the process for testing fonts is:
- Go search fonts in the web or locally
- Download the font, unzip it, copy to unity project assets folder
- create textmesh pro font asset for that font
- apply the font to some ui element
- Repeat for each tested font!!

### Features
- Preview local fonts easily
- Preview web fonts easily

### Notes
- Arrow keys scrolling the fonts list doesnt work properly (selected item is not always visible in the list, so use mouse to scroll)
- Fonts are temporarily added to Assets/Temp/ folder, so clean it up later (you can also press clear temp fonts)

### External resources
- Web fonts are taken from https://gwfh.mranftl.com/api/fonts

 ### Images
 ![Image](https://github.com/user-attachments/assets/8a7d8788-2387-4b3d-aca4-e179fb40cc7e)

 
