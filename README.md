# ASCOM-WUAPI-ObsCon
An ASCOM Observing Conditions driver that gets its information from Weather Underground.

----

An extension of Chris Rowland's idea to populate an ASCOM Observing Conditions interface with data from a weather API, rather than from phycially connected hardware and sensors, which utilizes the OpenWeatherMap API.

I felt there was an opportunity for this version, which uses the Weather Underground API, for 3 reasons :

* WU offers considerably more observing stations to choose from, with a community of over 300,000 submitting stations around the world.  This greatly increases the likelihood that a user can find a station within 10 miles of his or her observing location.

* Many of OWM's staitons update only hourly.

* Uploading to OWM requires user ability to control and direct the station's activities.  If the user had this ability, they would not need to use OWM to store the data, and could instead feed sensor readings straight to the ASCOM driver themselves.  Many users either own hardware that prohibits this, or do not know how to interface directly with their unit.  Frequently, however, these stations/users have simple, easily understood methods for connecting the station to WU.  This opens up "Own data use" to a great many users.

---

Users will require two things to configure the driver :

* A Weather Underground API Key, from any of WU's plans.  This can be obtained for free by creating a Weather Underground account, and then visiting https://www.wunderground.com/weather/api/

* Select a Weather Underground station ID.  Simply search for your location in the WU search bar, and then use the map to select your preferred station.  The station ID will be next to the name/location.

---

Currently there is no installer/executable.  That will be forthcoming shortly.  until then, feel free to download and build as you wish.
