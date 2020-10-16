using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscChanger.Models
{
    public class DiscChangerModelConverter: JsonConverter<DiscChangerModel>
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeof(DiscChangerModel).IsAssignableFrom(typeToConvert);

        public override DiscChangerModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }
            String "Name": "BluRay",
    "Key": "bluray",
    "Type": "Sony BDP-CX7000ES",




            reader.Read();
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            string propertyName = reader.GetString();
            if (propertyName != "TypeDiscriminator")
            {
                throw new JsonException();
            }

            reader.Read();
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException();
            }

            TypeDiscriminator typeDiscriminator = (TypeDiscriminator)reader.GetInt32();
            DiscChangerModel DiscChangerModel = typeDiscriminator switch
            {
                TypeDiscriminator.Customer => new Customer(),
                TypeDiscriminator.Employee => new Employee(),
                _ => throw new JsonException()
            };

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return DiscChangerModel;
                }

                if (reader.TokenType == JsonTokenType..PropertyName)
                {
                    propertyName = reader.GetString();
                    reader.Read();
                    switch (propertyName)
                    {
            "Name": "BluRay",
    "Key": "bluray",
    "Type": "Sony BDP-CX7000ES",
    "Connection": "SerialPort",
    "CommandMode": "BD1",
    "PortName": "COM3",
    "AdjustLastTrackLength": true,
    "ReverseDiscExistBytes": true,
    "HardwareFlowControl": true

                        case "CreditLimit":
                            decimal creditLimit = reader..GetDecimal();
                            ((Customer)DiscChangerModel).CreditLimit = creditLimit;
                            break;
                        case "OfficeNumber":
                            string officeNumber = reader.GetString();
                            ((Employee)DiscChangerModel).OfficeNumber = officeNumber;
                            break;
                        case "Name":
                            string name = reader.GetString();
                            DiscChangerModel.Name = name;
                            break;
                    }
                }
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, DiscChangerModel discChanger, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            if (discChanger is Customer customer)
            {
                writer.WriteNumber("TypeDiscriminator", (int)TypeDiscriminator.Customer);
                writer.WriteNumber("CreditLimit", customer.CreditLimit);
            }
            else if (discChanger is Employee employee)
            {
                writer.WriteNumber("TypeDiscriminator", (int)TypeDiscriminator.Employee);
                writer.WriteString("OfficeNumber", employee.OfficeNumber);
            }

            writer.WriteString("Name", discChanger.Name);

            writer.WriteEndObject();
        }
    }
}
