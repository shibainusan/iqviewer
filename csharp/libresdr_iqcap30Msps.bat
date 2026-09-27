::ECHO OFF
set SDR_ADDRESS=192.168.2.131
set SDR_FREQ=351000000
set SDR_BANDWIDTH=40000000
set SDR_SAMPLE_RATE=30720000
set SDR_TOTAL_SAMPLES=3072000
set SDR_GAIN=0

::RXLO Freq
::iio_attr -u ip:%SDR_ADDRESS% -c ad9361-phy altvoltage0 frequency %SDR_FREQ%
::TXLO Freq
::iio_attr -u ip:%SDR_ADDRESS% -c ad9361-phy altvoltage1 frequency %SDR_FREQ%

iio_attr -u ip:%SDR_ADDRESS% -c ad9361-phy voltage0 sampling_frequency %SDR_SAMPLE_RATE%
iio_attr -u ip:%SDR_ADDRESS% -c ad9361-phy voltage0 rf_bandwidth %SDR_BANDWIDTH%

iio_attr -u ip:%SDR_ADDRESS% -c ad9361-phy voltage0 gain_control_mode manual
iio_attr -u ip:%SDR_ADDRESS% -i -c ad9361-phy voltage0 hardwaregain %SDR_GAIN%

iio_readdev -u ip:%SDR_ADDRESS% -b 65536 -s %SDR_TOTAL_SAMPLES% cf-ad9361-lpc > .\iqcap30Msps.raw


