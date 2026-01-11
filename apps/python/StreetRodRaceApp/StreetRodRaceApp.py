##############################################################
# Street Rod Race App
# Crash detection and enforcement for Street Rod Manager
# Version: 1.0.0
##############################################################

import ac
import acsys
import os
import json
import sys
import math
import uuid
from datetime import datetime

# Script version
SCRIPT_VERSION = "1.0.0"
SCHEMA_VERSION = "1.0"

# Crash detection parameters
HARD_CRASH_THRESHOLD_G = 100.0  # Game over threshold
CRASH_COOLDOWN_SECONDS = 5.0    # Prevent duplicate detections

# Fixed-rate sampling interval (seconds)
SAMPLING_INTERVAL = 0.1  # Sample at 10Hz instead of per-frame

# App window
appWindow = 0

# Session state
session_id = None
session_active = False
session_start_time = None
session_end_time = None
session_duration_seconds = 0.0
accumulated_delta = 0.0

# Player tracking
player_car_id = 0
player_crashed = False

# Car data storage
car_data = {}

# UI elements
status_label = None


class CarData:
    """Data container for each car"""
    def __init__(self, car_id, driver_name, car_name):
        self.car_id = car_id
        self.driver_name = driver_name
        self.car_name = car_name

        # Performance tracking
        self.laps_completed = 0
        self.prev_lap_count = 0
        self.best_lap_time_ms = None
        self.total_race_time_ms = 0.0
        self.max_speed_kmh = 0.0
        self.distance_km = 0.0
        self.fuel_consumed_liters = 0.0
        self.prev_fuel = None
        self.final_position = None

        # Crash tracking
        self.prev_g_force_total = 0.0
        self.last_crash_check_time = -999.0
        self.crash_intensities = []  # List of all crashes
        self.crashed = False  # Hard crash flag
        self.crash_timestamp = None

    def to_dict(self):
        """Convert to output dictionary"""
        return {
            "driver_name": self.driver_name,
            "car_name": self.car_name,
            "performance": {
                "final_position": self.final_position,
                "laps_completed": self.laps_completed,
                "best_lap_time_ms": round(self.best_lap_time_ms, 2) if self.best_lap_time_ms else None,
                "total_race_time_ms": round(self.total_race_time_ms, 2),
                "max_speed_kmh": round(self.max_speed_kmh, 2),
                "distance_km": round(self.distance_km, 3),
                "fuel_consumed_liters": round(self.fuel_consumed_liters, 3)
            },
            "crash": {
                "crashed": self.crashed,
                "crash_intensities_g": [round(g, 2) for g in self.crash_intensities],
                "max_crash_intensity_g": round(max(self.crash_intensities), 2) if self.crash_intensities else 0.0,
                "crash_timestamp": self.crash_timestamp
            }
        }


def acMain(ac_version):
    """Initialize the app"""
    global appWindow, session_id, session_start_time, status_label

    try:
        # Generate unique session ID
        session_id = str(uuid.uuid4())
        session_start_time = datetime.utcnow().isoformat() + 'Z'

        # Create app window
        appWindow = ac.newApp("StreetRodRaceApp")
        ac.setSize(appWindow, 300, 80)
        ac.setTitle(appWindow, "Street Rod Manager")

        # Status label
        status_label = ac.addLabel(appWindow, "Race active - Drive safely")
        ac.setPosition(status_label, 10, 30)
        ac.setFontSize(status_label, 16)

        ac.log("Street Rod Race App: Initialized - Session ID: " + session_id)

        return "StreetRodRaceApp"
    except Exception as e:
        ac.log("Street Rod Race App ERROR in acMain: " + str(e))
        return "StreetRodRaceApp"


def acUpdate(deltaT):
    """Called every frame - fixed-rate sampling for performance"""
    global session_active, accumulated_delta, session_duration_seconds
    global car_data, player_car_id

    try:
        # Initialize session on first update
        if not session_active:
            session_active = True
            player_car_id = 0  # Player is always car 0

            # Initialize all cars
            total_cars = ac.getCarsCount()
            for car_id in range(total_cars):
                driver_name = ac.getDriverName(car_id)
                car_name = ac.getCarName(car_id)
                car_data[car_id] = CarData(car_id, driver_name, car_name)

                # Initialize fuel tracking
                try:
                    car_data[car_id].prev_fuel = ac.getCarState(car_id, acsys.CS.Gas)
                except:
                    pass

            ac.log("Street Rod Race App: Session started with {0} cars".format(total_cars))

        # Track total session duration
        session_duration_seconds += deltaT

        # Accumulate time for fixed-rate sampling
        accumulated_delta += deltaT

        # Only sample at fixed intervals
        if accumulated_delta >= SAMPLING_INTERVAL:
            update_all_telemetry(accumulated_delta)
            accumulated_delta = 0.0

        # Enforce crash mode if player crashed (every frame for responsive control lock)
        if player_crashed:
            enforce_crash_mode()

    except Exception as e:
        ac.log("Street Rod Race App ERROR in acUpdate: " + str(e))


def update_all_telemetry(deltaT):
    """Sample telemetry for all cars at fixed rate"""
    global car_data, session_duration_seconds

    try:
        current_time_seconds = session_duration_seconds

        for car_id, data in car_data.items():
            # Update race time
            data.total_race_time_ms += deltaT * 1000.0

            # Track laps
            current_lap_count = ac.getCarState(car_id, acsys.CS.LapCount)
            if current_lap_count > data.prev_lap_count:
                data.laps_completed = current_lap_count

                # Record lap time
                last_lap_time_ms = ac.getCarState(car_id, acsys.CS.LastLap)
                if last_lap_time_ms > 0:
                    if data.best_lap_time_ms is None or last_lap_time_ms < data.best_lap_time_ms:
                        data.best_lap_time_ms = last_lap_time_ms

            data.prev_lap_count = current_lap_count

            # Track max speed
            try:
                speed_ms = ac.getCarState(car_id, acsys.CS.SpeedMS)
                speed_kmh = speed_ms * 3.6
                if speed_kmh > data.max_speed_kmh:
                    data.max_speed_kmh = speed_kmh

                # Track distance (approximate)
                data.distance_km += (speed_ms * deltaT) / 1000.0
            except:
                pass

            # Track fuel consumption
            try:
                current_fuel = ac.getCarState(car_id, acsys.CS.Gas)
                if data.prev_fuel is not None and current_fuel < data.prev_fuel:
                    data.fuel_consumed_liters += (data.prev_fuel - current_fuel)
                data.prev_fuel = current_fuel
            except:
                pass

            # Crash detection (only if not already hard crashed)
            if not data.crashed:
                detect_crash_for_car(car_id, data, current_time_seconds)

    except Exception as e:
        ac.log("Street Rod Race App ERROR in update_all_telemetry: " + str(e))


def detect_crash_for_car(car_id, data, current_time_seconds):
    """Detect hard crashes using G-force delta for a specific car"""
    global player_crashed, status_label, player_car_id

    try:
        # Get G-force values [lateral, vertical, longitudinal]
        g_values = ac.getCarState(car_id, acsys.CS.AccG)
        g_x = g_values[0]
        g_y = g_values[1]
        g_z = g_values[2]

        # Calculate 3D magnitude
        total_g = math.sqrt(g_x**2 + g_y**2 + g_z**2)

        # Calculate change in G-force (crash intensity)
        g_change = abs(total_g - data.prev_g_force_total)

        # Check cooldown period
        time_since_last_check = current_time_seconds - data.last_crash_check_time

        # Detect hard crash
        if g_change >= HARD_CRASH_THRESHOLD_G and time_since_last_check >= CRASH_COOLDOWN_SECONDS:
            # Hard crash detected
            data.crashed = True
            data.crash_intensities.append(g_change)
            data.crash_timestamp = datetime.utcnow().isoformat() + 'Z'
            data.last_crash_check_time = current_time_seconds

            # If this is the player, activate crash mode
            if car_id == player_car_id:
                player_crashed = True

                # Update UI
                ac.setText(status_label, "RACE OVER - Hard crash detected")
                ac.setFontColor(status_label, 1.0, 0.0, 0.0, 1.0)  # Red

            ac.log("Street Rod Race App: HARD CRASH - {0} - Intensity: {1:.1f}G".format(data.driver_name, g_change))

        # Store current G-force for next frame
        data.prev_g_force_total = total_g

    except Exception as e:
        ac.log("Street Rod Race App ERROR in detect_crash_for_car: " + str(e))


def enforce_crash_mode():
    """Lock player controls when crashed"""
    try:
        # Force throttle to zero and brake to maximum
        ac.setGas(player_car_id, 0.0)
        ac.setBrake(player_car_id, 1.0)

    except Exception as e:
        ac.log("Street Rod Race App ERROR in enforce_crash_mode: " + str(e))


def acShutdown():
    """Called when session ends - write output file"""
    global session_end_time

    try:
        session_end_time = datetime.utcnow().isoformat() + 'Z'

        # Write session output
        write_session_output()

        ac.log("Street Rod Race App: Session ended - Output written")

    except Exception as e:
        ac.log("Street Rod Race App ERROR in acShutdown: " + str(e))


def write_session_output():
    """Write authoritative session data to JSON file"""
    global car_data

    try:
        # Get track info
        track_id = ac.getTrackName(0)
        track_layout = ac.getTrackConfiguration(0)
        if not track_layout:
            track_layout = None

        # Capture final positions for all cars
        for car_id, data in car_data.items():
            try:
                data.final_position = ac.getCarLeaderboardPosition(car_id)
                if data.final_position <= 0:
                    data.final_position = None
            except:
                data.final_position = None

        # Build participants array
        participants = []
        for car_id, data in car_data.items():
            participants.append(data.to_dict())

        # Build output data according to schema
        output_data = {
            "metadata": {
                "schema_version": SCHEMA_VERSION,
                "script_version": SCRIPT_VERSION,
                "source": "StreetRodRaceApp",
                "generated_at": datetime.utcnow().isoformat() + 'Z'
            },
            "session": {
                "session_id": session_id,
                "start_timestamp": session_start_time,
                "end_timestamp": session_end_time,
                "duration_seconds": round(session_duration_seconds, 2),
                "track_id": track_id,
                "track_layout": track_layout
            },
            "participants": participants
        }

        # Determine output directory
        documents_path = os.path.expanduser("~\\Documents")
        output_dir = os.path.join(documents_path, "Assetto Corsa", "out", "StreetRodRaceApp")

        # Create directory if it doesn't exist
        if not os.path.exists(output_dir):
            os.makedirs(output_dir)

        # Write output file with UUID as filename (atomic write)
        filename = "{0}.json".format(session_id)
        filepath = os.path.join(output_dir, filename)
        temp_filepath = filepath + ".tmp"

        # Write to temporary file first
        with open(temp_filepath, 'w') as f:
            json.dump(output_data, f, indent=2)

        # Atomic rename (safe even if AC crashes during write)
        os.replace(temp_filepath, filepath)

        ac.log("Street Rod Race App: Session data written to " + filepath)

    except Exception as e:
        ac.log("Street Rod Race App ERROR in write_session_output: " + str(e))
        import traceback
        ac.log("Street Rod Race App TRACEBACK: " + traceback.format_exc())
