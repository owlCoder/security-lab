# Izvor dijagrama AUD-01 / AUD-06

Dijagrami su ručno raspoređeni (fiksne koordinate) da bi bili čitljivi za KT1. Regenerisanje:

```
cd docs/components/audit/diagrams/src
python3 arch.py ../aud06-architecture.svg
python3 dfd.py  ../aud06-dfd.svg
rsvg-convert -z 2 ../aud06-architecture.svg -o ../aud06-architecture.png
rsvg-convert -z 2 ../aud06-dfd.svg -o ../aud06-dfd.png
```
