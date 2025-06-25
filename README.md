# Rattrapage – *EasySave v3*

Dépôt contenant la version **corrigée et fonctionnelle** d’EasySave pour le module Génie Logiciel.

---

## 1. Où lancer le programme ?

| Composant                      | Chemin de l’exécutable                                                                                                   |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------ |
| **EasySave (GUI)**             | `Versions/EasySave_G3_V3_0/bin/Release/net8.0-windows/EasySave_G3_V3_0.exe`                                              |
| **CryptoSoft** (chiffrement)   | `Versions/CryptoSoft/bin/Release/net8.0/CryptoSoft.exe`                                                                  |

> Copiez simplement le dossier où vous voulez : tous les chemins internes sont maintenant **relatifs** au dossier de l’exécutable.

---

## 2. Points corrigés

| Exigence du rattrapage                                                                         | Solution mise en place                                                                                               |
| ---------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| DLL de logs **JSON + XML**                                                                     | `LogEntry` écrit désormais les deux formats.                                                                          |
| Fonctionnement **portable dans n’importe quel répertoire**                                    | `AppPaths.cs` calcule tous les chemins via `AppContext.BaseDirectory`.                                                |
| **CryptoSoft mono-instance**                                                                   | Mutex global dans CryptoSoft ; EasySave attend si l’outil est déjà lancé.                                            |
| **Accès concurrent** aux fichiers de log                                                       | Mutex global + `lock` local autour de l’écriture pour éviter tout écrasement.                                        |
| **Diagrammes UML** cohérents                                                                   | Nouveaux diagrammes placés dans `Diagrammes UML/diagramme uml V1.1/`.                                                |
| Contrainte « **fichiers prioritaires** » (version 3)                                           | Classe `PriorityManager` : tant qu’il reste un fichier prioritaire, aucun fichier non prioritaire n’est copié.       |

---

## 3. Arborescence simplifiée

