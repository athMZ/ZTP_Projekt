# Projekt 1 - Pomiar wydajności filtra Laplace’a (5x5) w aplikacji do przetwarzania obrazów

Ten projekt służy do pomiaru wydajności aplikacji przetwarzającej obrazy z użyciem filtra Laplace’a o rozmiarze 5x5. Program został przygotowany z myślą o testowaniu różnych konfiguracji wpływających na zużycie pamięci i wydajność.

Opracował Mikołaj Zuziak

## Linki do materiałów:

- 📄 [Opracowanie](https://github.com/athMZ/ZTP_Projekt/blob/main/ztp-proj1.pdf)  
- 📊 [Przykładowe pomiary z dotMemory](https://github.com/athMZ/ZTP_Projekt/blob/main/samples)  
- ⚙️ [Ustawienia projektu](https://github.com/athMZ/ZTP_Projekt/blob/main/ZTP_Projekt_1%2FZTP_Projekt_1.csproj)  
- 💻 [Kod źródłowy](https://github.com/athMZ/ZTP_Projekt/blob/main/ZTP_Projekt_1/Program.cs)

## Konfiguracja za pomocą zmiennych środowiskowych

Program wykorzystuje zmienne środowiskowe do konfiguracji działania, co umożliwia łatwe i elastyczne testowanie różnych scenariuszy.

Przykładowa konfiguracja:
```
ENABLE_PARALLEL=true
VERSION_MANAGED=false
GC_COLLECT=true
COMPACT_ONCE=true
DISPOSE=true
USE_FIXED=true
USE_POOLING=true
SUSTAINED_LOW_LATENCY=true
```

---

# Projekt 2 - System Rozproszonego Przetwarzania Obrazów

Opracował Mikołaj Zuziak

- 📄 [Opracowanie]()   
- 💻 [Kod źródłowy - Serwer]()
- 💻 [Kod źródłowy - Klient]()