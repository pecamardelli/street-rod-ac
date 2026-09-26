"""
Runs the race mode's engine heat, oil, fuel and dirt (apps/new-modes/sr_race/mode.lua, engineHeat) in the stub world of
test_chase.py, without Assetto Corsa.

It checks the mode's own logic: a factory-cooled car stays cool, an engine cooled too little for what it makes runs
hot, goes flat, cooks and boils over, a stock sump starves in a long hard corner where a deep one does not, a tank that
runs dry puts the car out, and the fuel and dirt the career sends go in and come back in the result. Not how hot a real
engine gets, which only the numbers in the mode (and the game) can say.

    python tools/sr_race_harness/test_heat.py
"""

from test_chase import run, check

ROAD = {'STREET_ROD.RACE_TYPE': 'ROAD'}


def road(extra, **kwargs):
    ini = dict(ROAD)
    ini.update(extra)
    # The player at 30 m/s, the rival behind at 20: an 8 km loop is four and a half minutes of racing
    return run(kwargs.pop('name', 'heat'), 2, ini, lambda t: 30, lambda t: 20, lambda t, i: 0, **kwargs)


def flat_out(g=0.0):
    return lambda i, t: (1.0, 0.95, g)


def player(result):
    return result['participants'][0]


def a_factory_cooled_car_stays_cool():
    result, log, _, _ = road({'STREET_ROD.CAR_0_COOLING': '1,0.35,1.05'}, name='factory', max_seconds=700,
                             length=8000.0, engine=flat_out())
    heat = player(result)['condition']['heat']
    check('the race was finished', result['session']['end_reason'] == 'FINISHED', log)
    check(f'flat out for four minutes the water stayed under 105 ({heat["peak_water_c"]})', heat['peak_water_c'] < 105, log)
    check('nothing cooked', heat['heat_life_lost'] == 0 and heat['overheated_s'] == 0, log)
    check('no warning', 'Running hot' not in log, log)


def an_undercooled_engine_cooks_and_boils_over():
    # A third of the cooling its engine needs: it warns, fades, cooks and boils over before the line
    result, log, _, _ = road({'STREET_ROD.CAR_0_COOLING': '0.3,0.35,0.8'}, name='cooked', max_seconds=700,
                             length=8000.0, engine=flat_out())
    me = player(result)
    heat = me['condition']['heat']
    check('the player broke down', result['session']['end_reason'] == 'BROKE_DOWN', log)
    check(f'the engine boiled over ({me.get("breakdown")})', me.get('breakdown') == 'OVERHEAT', log)
    check('the player was warned, then told it was overheating', 'Running hot' in log and 'Overheating!' in log, log)
    check('it boiled over', 'boiled over at' in log, log)
    check(f'the heat took the whole life, and no more ({heat["heat_life_lost"]})', 999 <= heat['heat_life_lost'] <= 1001, log)
    check('the engine is dead', me['condition']['engine_life'] <= 0, log)
    check('it ran hot for a while', heat['overheated_s'] > 10 and heat['peak_water_c'] >= 135, log)


def a_warm_engine_goes_flat_but_lives():
    # 70% of what it needs: hot enough to go flat at the top, not hot enough to cook
    result, log, _, _ = road({'STREET_ROD.CAR_0_COOLING': '0.7,0.35,0.8'}, name='warm', max_seconds=700,
                             length=8000.0, engine=flat_out())
    heat = player(result)['condition']['heat']
    check('the race was finished', result['session']['end_reason'] == 'FINISHED', log)
    check(f'it ran hot ({heat["peak_water_c"]})', HOT < heat['peak_water_c'] < 118 and heat['overheated_s'] > 0, log)
    check(f'and went flat at the top ({heat["peak_fade"]})', heat['peak_fade'] > 0, log)
    check('but nothing cooked', heat['heat_life_lost'] == 0, log)


def a_hot_engine_cooks_some_and_makes_the_line():
    # 60% of what it needs, over two minutes: it cooks for the last forty seconds or so and gets home hurt
    result, log, _, _ = road({'STREET_ROD.CAR_0_COOLING': '0.6,0.35,0.8'}, name='cooking', max_seconds=400,
                             length=4000.0, engine=flat_out())
    me = player(result)
    heat = me['condition']['heat']
    check('the race was finished', result['session']['end_reason'] == 'FINISHED', log)
    check(f'some life went ({heat["heat_life_lost"]})', 50 < heat['heat_life_lost'] < 900, log)
    check('the engine life shows it', me['condition']['engine_life'] < 1000 - 50, log)


def a_drag_race_is_too_short_to_overheat():
    # Twelve seconds flat out even without a radiator: an engine takes a minute to heat through
    result, log, _, _ = road({'STREET_ROD.CAR_0_COOLING': '0.1,0.1,0.8'}, name='short', max_seconds=60, length=400.0,
                             engine=flat_out())
    heat = player(result)['condition']['heat']
    check('finished', result['session']['end_reason'] == 'FINISHED', log)
    check(f'no harm done ({heat["peak_water_c"]})', heat['peak_water_c'] < HOT and heat['heat_life_lost'] == 0, log)


def a_stock_sump_starves_in_a_long_corner():
    result, log, _, _ = road({'STREET_ROD.CAR_0_COOLING': '1,0.35,1.05'}, name='surge', engine=flat_out(g=1.15))
    heat = player(result)['condition']['heat']
    check(f'the oil ran short ({heat["lowest_oil_supply"]})', heat['lowest_oil_supply'] < 0.5, log)
    check('the bottom end took it', heat['oil_starved_s'] > 1 and heat['oil_life_lost'] > 0, log)
    check('the player saw the oil light', 'Oil pressure!' in log, log)


def a_deep_sump_holds_its_oil():
    result, log, _, _ = road({'STREET_ROD.CAR_0_COOLING': '1,0.35,1.25'}, name='deep', engine=flat_out(g=1.15))
    heat = player(result)['condition']['heat']
    check('the oil stayed', heat['lowest_oil_supply'] == 1 and heat['oil_life_lost'] == 0, log)


def a_surge_off_the_throttle_costs_nothing():
    # Long hard braking: the pickup sucks air, but a closed throttle asks little of the bearings
    result, log, _, _ = road({'STREET_ROD.CAR_0_COOLING': '1,0.35,1.05'}, name='braking',
                             engine=lambda i, t: (0.0, 0.8, 1.3))
    heat = player(result)['condition']['heat']
    check('the oil ran short', heat['lowest_oil_supply'] < 0.5, log)
    check('but nothing was worn', heat['oil_life_lost'] == 0 and heat['oil_starved_s'] == 0, log)


def a_dry_tank_puts_the_car_out():
    result, log, _, _ = road({'STREET_ROD.CAR_0_FUEL': '0.5'}, name='dry', fuel_burn=0.05, engine=flat_out())
    me = player(result)
    check('the player broke down', result['session']['end_reason'] == 'BROKE_DOWN', log)
    check(f'out of gas ({me.get("breakdown")})', me.get('breakdown') == 'FUEL', log)
    check('the tank is empty', me['condition']['fuel_litres'] <= 0.02, log)


def fuel_and_dirt_go_in_and_come_back():
    result, log, _, _ = road({'STREET_ROD.CAR_0_FUEL': '-1', 'STREET_ROD.CAR_0_DIRT': '0.4',
                              'STREET_ROD.CAR_1_FUEL': '12.5'}, name='tank')
    me, rival = player(result)['condition'], result['participants'][1]['condition']
    check('a full tank for the player', me['fuel_litres'] == 60 and me['max_fuel_litres'] == 60, log)
    check('the dirt went in and came back', me['dirt'] == 0.4, log)
    check("the rival's fuel went in", rival['fuel_litres'] == 12.5, log)
    check('a car the career did not rate is taken as a factory car', 'Car 1: cooling 1.00, fan 0.35, sump 1.05 g' in log, log)


HOT = 110

if __name__ == '__main__':
    tests = [a_factory_cooled_car_stays_cool, an_undercooled_engine_cooks_and_boils_over, a_warm_engine_goes_flat_but_lives,
             a_hot_engine_cooks_some_and_makes_the_line,
             a_drag_race_is_too_short_to_overheat, a_stock_sump_starves_in_a_long_corner, a_deep_sump_holds_its_oil,
             a_surge_off_the_throttle_costs_nothing, a_dry_tank_puts_the_car_out, fuel_and_dirt_go_in_and_come_back]
    for test in tests:
        test()
        print(f'ok  {test.__name__}')
    print(f'{len(tests)} passed')
