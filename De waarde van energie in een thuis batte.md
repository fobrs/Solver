De waarde van energie in een thuis batterij,
    voor en na saldering met een dynamisch contract

Voorbeeld:

PV: 1500 Watt
Consumptie: 500 Watt
PVoverschot: 1000 Watt
Etax: 0.1228 euro
Max Laadcpaciteit batterij: 1000 Watt
Max Ontlaadcpaciteit batterij: 1000 Watt
SoC (state of charge): 0 KWh
We vergeten even de efficiencie van de batterij


1. Opladen van het stroomnet (als er geen PV is)
    - neem actuele stroomprijs

2. Opladen van PV (als er overschot is)
    - Trek van PV verwachte directe consumptie af: PVoverschot
    - PVoverschot zou bij geen batterij worden teruggeleverd, dus:
        - Saldering intact: neem prijs van teruggeleverde energie
        - Geen saldering: neem prijs van teruggeleverde energie
            verminderd met de energiebelasting Etax

3. Ontladen:
    - neem actuele stroomprijs

Als punt 2 een correcte aanname is dan volgt het volgende voor
3 hypthetische tijdvakken van een uur bij uurprijzen (andere
tijdvakken vergeten we even).

Saldering:                                 Geen Saldering:

Opladen:

uur: 4:00 - 5:00                                    
PV: 0 Watt
Consumptie: 500 Watt
prijs: 0.20 euro/kWh (inc. tax)
kosten opladen bij 1000 Watt: 0.20 euro    0.20

uur: 13:00 - 14:00
PV: 1500 Watt
Consumptie: 500 Watt
PVoverschot: 1000 Watt
prijs: 0.20 euro/kWh (inc. tax)            0.0772 euro (excl tax)
kosten opladen bij 1000 Watt: 0.20 euro    0.20 - 0.1228 = 0.0772 euro

SoC: 1000 + 1000 = 2.0 kWh                 2.0 kWh
Kosten: 0.20 + 0.20 = 0.435 euro           0.2 + 0.0772 = 0.2772 euro

Ontladen:

uur: 18:00 - 20:00
PV: 0 Watt
Consumptie: 1000 Watt
PVoverschot: 0 Watt
prijs: 0.40 euro/kWh
Besparing: 2.0 * 0.40 = 0.80 euro          0.80  euro

Winst: 0.80 - 0.40 = 0.40 euro             0.80 - 0.2772 = 0.5228 euro

Conclusie:
Afschaffen van saldering levert met een thuisbatterij meer winst op!!!

